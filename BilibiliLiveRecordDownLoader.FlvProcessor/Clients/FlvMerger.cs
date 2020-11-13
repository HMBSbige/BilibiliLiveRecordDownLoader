using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagPackets;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Clients
{
	public class FlvMerger : IFlvMerger
	{
		private readonly ILogger _logger;

		private long _fileSize;
		private long _current;
		private long _last;

		public double Progress => Interlocked.Read(ref _current) / (double)_fileSize;

		private readonly BehaviorSubject<double> _currentSpeed = new BehaviorSubject<double>(0.0);
		public IObservable<double> CurrentSpeed => _currentSpeed.AsObservable();

		private readonly BehaviorSubject<string> _status = new BehaviorSubject<string>(string.Empty);
		public IObservable<string> Status => _status.AsObservable();

		public int BufferSize { get; set; } = 4096;

		public bool IsAsync { get; set; } = false;

		private readonly List<string> _files = new List<string>();
		public IEnumerable<string> Files => _files;

		public FlvMerger(ILogger<FlvMerger> logger)
		{
			_logger = logger;
		}

		public void Add(string path)
		{
			_files.Add(path);
		}

		public void AddRange(IEnumerable<string> path)
		{
			_files.AddRange(path);
		}

		public async ValueTask MergeAsync(string path, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			_fileSize = Files.Sum(file => new FileInfo(file).Length);

			var sw = Stopwatch.StartNew();
			using var speedMonitor = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
			{
				var last = Interlocked.Read(ref _last);
				_currentSpeed.OnNext(last / sw.Elapsed.TotalSeconds);
				sw.Restart();
				Interlocked.Add(ref _last, -last);
			});

			await using var outFile = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, IsAsync);

			var header = new FlvHeader();
			var metadataHeader = new FlvTagHeader();

			await using (var f0 = new FileStream(Files.First(), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
			{
				using (var headerBuffer = MemoryPool<byte>.Shared.Rent(header.Size))
				{
					var memory = headerBuffer.Memory.Slice(0, header.Size);

					_status.OnNext(@"读 Header");
					f0.Read(memory.Span);
					header.Read(memory.Span);
					_logger.LogDebug($@"{header.Signature} {header.Version} {header.Flags} {header.HeaderSize}");

					_status.OnNext(@"写 Header");
					WriteWithProgress(outFile, memory.Span, token);
				}

				using (var metadataHeaderBuffer = MemoryPool<byte>.Shared.Rent(metadataHeader.Size))
				{
					var memory = metadataHeaderBuffer.Memory.Slice(0, metadataHeader.Size);

					_status.OnNext(@"读 Metadata");
					f0.Read(memory.Span);
					metadataHeader.Read(memory.Span);

					if (metadataHeader.PayloadInfo.PacketType == PacketType.AMF_Metadata)
					{
						_status.OnNext(@"写 MetaData header");
						WriteWithProgress(outFile, memory.Span, token);

						_status.OnNext(@"写 MetaData payload 和 MetaData size");
						CopyFixedSize(f0, outFile, (int)metadataHeader.PayloadInfo.PayloadSize + sizeof(uint), token);
					}
					else
					{
						_logger.LogWarning(@"First packet is not a metadata packet");
						metadataHeader.Timestamp.Data = 0;
						metadataHeader.PayloadInfo.PacketType = PacketType.AMF_Metadata;
						var metadata = new AMFMetadata();
						metadataHeader.PayloadInfo.PayloadSize = (uint)metadata.Size;

						_status.OnNext(@"写 MetaData header");
						WriteWithProgress(outFile, metadataHeader.ToMemory(metadataHeaderBuffer.Memory).Span, token);

						_status.OnNext(@"写 MetaData payload");
						using (var metadataBuffer = MemoryPool<byte>.Shared.Rent(metadata.Size))
						{
							WriteWithProgress(outFile, metadata.ToMemory(metadataBuffer.Memory).Span, token);
						}

						_status.OnNext(@"写 MetaData size");
						using var metadataSizeBuffer = MemoryPool<byte>.Shared.Rent(sizeof(uint));
						BinaryPrimitives.WriteUInt32BigEndian(metadataSizeBuffer.Memory.Span, (uint)metadata.Size + (uint)metadataHeader.Size);
						WriteWithProgress(outFile, metadataSizeBuffer.Memory.Slice(0, sizeof(uint)).Span, token);
					}
				}
			}

			var timestamp = 0u;
			var allRead = 0L;

			var tagHeader = new FlvTagHeader(); // 循环内每次 new 一个 tag 的话开销过大
			using var tagHeaderBuffer = MemoryPool<byte>.Shared.Rent(tagHeader.Size);
			var tagHeaderMemory = tagHeaderBuffer.Memory.Slice(0, tagHeader.Size);

			var i = 0ul;
			foreach (var file in Files)
			{
				++i;
				_status.OnNext($@"正在合并 Flv ({i}/{_files.Count})");
				var currentTimestamp = 0u;

				await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
				var length = fs.Length;

				fs.Seek(header.Size, SeekOrigin.Begin);
				long read = header.Size;

				while (read < length)
				{
					fs.Read(tagHeaderMemory.Span);
					tagHeader.Read(tagHeaderMemory.Span);

					currentTimestamp = Math.Max(currentTimestamp, tagHeader.Timestamp.Data);

					switch (tagHeader.PayloadInfo.PacketType)
					{
						case PacketType.AMF_Metadata:
						{
							fs.Seek((int)tagHeader.PayloadInfo.PayloadSize + sizeof(uint), SeekOrigin.Current);
							break;
						}
						case PacketType.AudioPayload:
						case PacketType.VideoPayload:
						{
							// 重写时间戳
							tagHeader.Timestamp.Data += timestamp;

							// 写 tag header
							WriteWithProgress(outFile, tagHeader.ToMemory(tagHeaderMemory).Span, token);

							// 复制 Payload 和 tag 大小
							CopyFixedSize(fs, outFile, (int)tagHeader.PayloadInfo.PayloadSize + sizeof(uint), token);
							break;
						}
						default:
						{
							_logger.LogWarning($@"Unsupported tag type: {tagHeader.PayloadInfo.PacketType}");
							break;
						}
					}

					read += tagHeader.Size + (int)tagHeader.PayloadInfo.PayloadSize + sizeof(uint);
				}

				timestamp += currentTimestamp;
				allRead += read;
				Interlocked.Exchange(ref _current, allRead);
			}

			_status.OnNext(@"正在修复时长...");
			FixDuration(outFile, header.Size, (int)metadataHeader.PayloadInfo.PayloadSize + metadataHeader.Size + sizeof(uint), TimeSpan.FromMilliseconds(timestamp).TotalSeconds);
			_status.OnNext(@"已完成 Flv 合并");
		}

		private void CopyFixedSize(Stream source, Stream dst, int size, CancellationToken token)
		{
			using var memory = MemoryPool<byte>.Shared.Rent(Math.Min(BufferSize, size));

			var span = memory.Memory.Span;
			while (size > 0)
			{
				var shouldRead = Math.Min(size, span.Length);

				var read = source.Read(span.Slice(0, shouldRead));
				if (read <= 0)
				{
					break;
				}

				size -= read;
				WriteWithProgress(dst, span.Slice(0, read), token);
			}
		}

		private void WriteWithProgress(Stream dst, ReadOnlySpan<byte> buffer, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			dst.Write(buffer);
			ReportProgress(buffer.Length);
		}

		private void ReportProgress(long length)
		{
			Interlocked.Add(ref _last, length);
			Interlocked.Add(ref _current, length);
		}

		/// <summary>
		/// {sizeof("duration")}{duration}{double}
		/// </summary>
		private static readonly Memory<byte> 特征duration = new byte[] { 0x08, 0x64, 0x75, 0x72, 0x61, 0x74, 0x69, 0x6F, 0x6E, 0x00 };

		private void FixDuration(Stream file, int offset, int size, double duration)
		{
			file.Seek(offset, SeekOrigin.Begin);

			const int durationSize = sizeof(double);

			using var memory = MemoryPool<byte>.Shared.Rent(size + durationSize);

			var span = memory.Memory.Slice(0, size).Span;

			file.Read(span);

			var index = span.IndexOf(特征duration.Span);
			if (index < 0)
			{
				_logger.LogWarning(@"找不到 duration 字段");
				return;
			}

			var i = offset + span.IndexOf(特征duration.Span) + 特征duration.Length;

			var outBytes = memory.Memory.Slice(size, durationSize).Span;

			BinaryPrimitives.WriteDoubleBigEndian(outBytes, duration);

			file.Seek(i, SeekOrigin.Begin);
			WriteWithProgress(file, outBytes, default);
		}

		public ValueTask DisposeAsync()
		{
			_currentSpeed.OnCompleted();
			_status.OnCompleted();

			return default;
		}
	}
}
