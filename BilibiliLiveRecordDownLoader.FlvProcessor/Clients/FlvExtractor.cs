using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Clients
{
	public class FlvExtractor : IFlvExtractor
	{
		private readonly ILogger _logger;

		private long _fileSize;
		private long _current;
		private long _last;
		private long _read;

		public double Progress => Interlocked.Read(ref _current) / (double)_fileSize;

		private readonly BehaviorSubject<double> _currentSpeed = new(0.0);//TODO
		public IObservable<double> CurrentSpeed => _currentSpeed.AsObservable();

		private readonly BehaviorSubject<string> _status = new(string.Empty); //TODO
		public IObservable<string> Status => _status.AsObservable();

		public int BufferSize { get; init; } = 4096;

		public bool IsAsync { get; init; }

		public string? OutputDir { get; init; }

		public string? OutputVideo { get; private set; }

		public string? OutputAudio { get; private set; }

		private static readonly string[] OutputExtensions = { @".avi", @".mp3", @".264", @".aac", @".spx", @".txt" };

		public FlvExtractor(ILogger<FlvExtractor> logger)
		{
			_logger = logger;
		}

		public async ValueTask ExtractAsync(string path, CancellationToken token)
		{
			await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
			_fileSize = fs.Length;
			_read = 0L;

			var header = new FlvHeader();
			using (var headerBuffer = MemoryPool<byte>.Shared.Rent(header.Size))
			{
				var memory = headerBuffer.Memory.Slice(0, header.Size);

				_status.OnNext(@"读 Header");
				ReadWithProgress(fs, memory.Span, token);
				header.Read(memory.Span);

				_logger.LogDebug($@"{header.Signature} {header.Version} {header.Flags} {header.HeaderSize}");

				if (header.Signature != @"FLV" || header.Version != 1)
				{
					throw new NotSupportedException(@"This isn't a FLV file!");
				}
			}

			var fileExtension = Path.GetExtension(path).ToLowerInvariant();
			if (OutputExtensions.Any(ext => ext == fileExtension))
			{
				throw new NotSupportedException(@"Unsupported extension");
			}

			if (!Directory.Exists(OutputDir))
			{
				throw new DirectoryNotFoundException(@"Output directory doesn't exist.");
			}

			var tagHeader = new FlvTagHeader();
			using var tagHeaderBuffer = MemoryPool<byte>.Shared.Rent(tagHeader.Size);
			var tagHeaderMemory = tagHeaderBuffer.Memory.Slice(0, tagHeader.Size);

			while (_read + tagHeader.Size < _fileSize)
			{
				// 读 tag header
				ReadWithProgress(fs, tagHeaderMemory.Span, token);
				tagHeader.Read(tagHeaderMemory.Span);

				var payloadSize = (int)tagHeader.PayloadInfo.PayloadSize;
				if (payloadSize == 0)
				{
					continue;
				}

				if (_fileSize - _read < payloadSize)
				{
					break;
				}

				using var payloadBuffer = MemoryPool<byte>.Shared.Rent(payloadSize + sizeof(uint));
				var payloadMemory = payloadBuffer.Memory.Slice(0, payloadSize);
				var tagSizeMemory = payloadBuffer.Memory.Slice(payloadSize, sizeof(uint));
				// 读 payload
				ReadWithProgress(fs, payloadMemory.Span, token);

				// tag size
				ReadWithProgress(fs, tagSizeMemory.Span, token);
				var tagSize = BinaryPrimitives.ReadUInt32BigEndian(tagSizeMemory.Span);
				if (tagSize != payloadSize + tagHeader.Size)
				{
					_logger.LogWarning($@"Tag Size({tagSize}) ≠ PayloadSize({payloadSize})+TagHeaderSize({tagHeader.Size})");
				}

				switch (tagHeader.PayloadInfo.PacketType)
				{
					case PacketType.AudioPayload:
					{
						//TODO
						break;
					}
					case PacketType.VideoPayload when payloadMemory.Span[0].IsFrameType():
					{
						//TODO
						break;
					}
				}
			}

			throw new NotImplementedException();
		}

		private void ReadWithProgress(Stream stream, Span<byte> buffer, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			var length = stream.Read(buffer);
			_read += length;
			ReportProgress(length);
		}

		private void ReportProgress(long length)
		{
			Interlocked.Add(ref _last, length);
			Interlocked.Add(ref _current, length);
		}

		public ValueTask DisposeAsync()
		{
			throw new NotImplementedException();
		}
	}
}
