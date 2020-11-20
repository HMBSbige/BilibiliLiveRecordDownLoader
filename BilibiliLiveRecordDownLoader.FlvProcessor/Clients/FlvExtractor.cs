using BilibiliLiveRecordDownLoader.FlvProcessor.AudioWrites;
using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;
using BilibiliLiveRecordDownLoader.FlvProcessor.Utils;
using BilibiliLiveRecordDownLoader.FlvProcessor.VideoWriters;
using BilibiliLiveRecordDownLoader.Shared;
using BilibiliLiveRecordDownLoader.Shared.Abstracts;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Clients
{
	public class FlvExtractor : ProgressBase, IFlvExtractor
	{
		private readonly ILogger _logger;

		private long _read;

		public int BufferSize { get; init; } = 4096;

		public bool IsAsync { get; init; }

		public string? OutputDir { get; init; }

		public string? OutputVideo { get; private set; }

		public string? OutputAudio { get; private set; }

		private static readonly string[] OutputExtensions = { @".avi", @".mp3", @".264", @".aac", @".spx", @".txt" };
		private IAudioWriter? _audioWriter;
		private IVideoWriter? _videoWriter;
		private readonly List<uint> _videoTimeStamps = new();
		private string? _outputBasePath;

		public FlvExtractor(ILogger<FlvExtractor> logger)
		{
			_logger = logger;
		}

		public async ValueTask ExtractAsync(string path, CancellationToken token)
		{
			await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
			FileSize = fs.Length;
			_read = 0L;

			var header = new FlvHeader();
			using (var headerBuffer = MemoryPool<byte>.Shared.Rent(header.Size))
			{
				var memory = headerBuffer.Memory.Slice(0, header.Size);

				StatusSubject.OnNext(@"读 Header");
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

			if (!Directory.Exists(OutputDir) || OutputDir is null)
			{
				throw new DirectoryNotFoundException(@"Output directory doesn't exist.");
			}

			_outputBasePath = Path.Combine(OutputDir, Path.GetFileNameWithoutExtension(path));

			var sw = Stopwatch.StartNew();
			using var speedMonitor = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
			{
				var last = Interlocked.Read(ref Last);
				CurrentSpeedSubject.OnNext(last / sw.Elapsed.TotalSeconds);
				sw.Restart();
				Interlocked.Add(ref Last, -last);
			});

			var tagHeader = new FlvTagHeader();
			using var tagHeaderBuffer = MemoryPool<byte>.Shared.Rent(tagHeader.Size);
			var tagHeaderMemory = tagHeaderBuffer.Memory.Slice(0, tagHeader.Size);

			StatusSubject.OnNext(@"正在提取...");
			while (_read + tagHeader.Size < FileSize)
			{
				// 读 tag header
				ReadWithProgress(fs, tagHeaderMemory.Span, token);
				tagHeader.Read(tagHeaderMemory.Span);

				var payloadSize = (int)tagHeader.PayloadInfo.PayloadSize;
				if (payloadSize == 0)
				{
					continue;
				}

				if (FileSize - _read < payloadSize)
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
						_audioWriter ??= GetAudioWriter(_outputBasePath, payloadMemory.Span[0]);
						_audioWriter.Write(payloadMemory[1..], tagHeader.Timestamp.Data);
						break;
					}
					case PacketType.VideoPayload when payloadMemory.Span[0].IsFrameType():
					{
						_videoWriter ??= GetVideoWriter(_outputBasePath, payloadMemory.Span[0]);
						var timeStamp = tagHeader.Timestamp.Data;
						_videoTimeStamps.Add(timeStamp);
						_videoWriter.Write(payloadMemory[1..], timeStamp, payloadMemory.Span[0].ToFrameType());
						break;
					}
				}
			}

			var averageFrameRate = CalculateAverageFrameRate();
			var trueFrameRate = CalculateTrueFrameRate();
			await CloseOutput(averageFrameRate, false);

			_logger.LogDebug($@"平均帧数：{averageFrameRate}");
			_logger.LogDebug($@"真实帧数：{trueFrameRate}");
			StatusSubject.OnNext(@"已完成");
		}

		private IAudioWriter GetAudioWriter(in string path, byte mediaInfo)
		{
			var format = mediaInfo.ToSoundFormat();

			switch (format)
			{
				case SoundFormat.AAC:
				{
					OutputAudio = Path.ChangeExtension(path, @".aac");
					return new AACWriter(OutputAudio, IsAsync, BufferSize);
				}
				default:
				{
					_logger.LogWarning($@"Unable to extract audio ({format} is unsupported).");
					break;
				}
			}
			return new DefaultAudioWrite();
		}

		private IVideoWriter GetVideoWriter(in string path, byte mediaInfo)
		{
			var codecId = mediaInfo.ToCodecID();

			switch (codecId)
			{
				case CodecID.AVC:
				{
					OutputVideo = Path.ChangeExtension(path, @".264");
					return new H264Writer(OutputVideo, IsAsync, BufferSize);
				}
				default:
				{
					_logger.LogWarning($@"Unable to extract video ({codecId} is unsupported).");
					break;
				}
			}

			return new DefaultVideoWriter();
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
			Interlocked.Add(ref Last, length);
			Interlocked.Add(ref Current, length);
		}

		private FractionUInt32? CalculateAverageFrameRate()
		{
			var frameCount = _videoTimeStamps.Count;

			if (frameCount > 1)
			{
				var frameRate = new FractionUInt32(
					(uint)(frameCount - 1) * 1000,
					_videoTimeStamps.Last() - _videoTimeStamps.First());
				return frameRate;
			}

			return null;
		}

		private FractionUInt32? CalculateTrueFrameRate()
		{
			var deltaCount = new Dictionary<uint, uint>();
			uint delta, count;

			// Calculate the distance between the timestamps, count how many times each delta appears
			for (var i = 1; i < _videoTimeStamps.Count; ++i)
			{
				var deltaS = (int)(_videoTimeStamps[i] - (long)_videoTimeStamps[i - 1]);

				if (deltaS <= 0)
				{
					continue;
				}

				delta = (uint)deltaS;

				if (deltaCount.ContainsKey(delta))
				{
					deltaCount[delta] += 1;
				}
				else
				{
					deltaCount.Add(delta, 1);
				}
			}

			var threshold = _videoTimeStamps.Count / 10;
			var minDelta = uint.MaxValue;

			// Find the smallest delta that made up at least 10% of the frames (grouping in delta+1
			// because of rounding, e.g. a NTSC video will have deltas of 33 and 34 ms)
			foreach (var (key, value) in deltaCount)
			{
				delta = key;
				count = value;

				if (deltaCount.ContainsKey(delta + 1))
				{
					count += deltaCount[delta + 1];
				}

				if (count >= threshold && delta < minDelta)
				{
					minDelta = delta;
				}
			}

			// Calculate the frame rate based on the smallest delta, and delta+1 if present
			if (minDelta != uint.MaxValue)
			{
				count = deltaCount[minDelta];
				var totalTime = minDelta * count;
				var totalFrames = count;

				if (deltaCount.ContainsKey(minDelta + 1))
				{
					count = deltaCount[minDelta + 1];
					totalTime += (minDelta + 1) * count;
					totalFrames += count;
				}

				if (totalTime != 0)
				{
					return new FractionUInt32(totalFrames * 1000, totalTime);
				}
			}

			// Unable to calculate frame rate
			return null;
		}

		private async ValueTask CloseOutput(FractionUInt32? frameRate, bool disposing)
		{
			if (_videoWriter is not null)
			{
				await _videoWriter.FinishAsync(frameRate ?? new FractionUInt32(25, 1));
				if (disposing)
				{
					DeleteFileWithRetryAsync(_videoWriter.Path).NoWarning();
				}
				_videoWriter = null;
			}

			if (_audioWriter is not null)
			{
				await _audioWriter.DisposeAsync();
				if (disposing)
				{
					DeleteFileWithRetryAsync(_audioWriter.Path).NoWarning();
				}
				_audioWriter = null;
			}
		}

		private async ValueTask DeleteFileWithRetryAsync(string? filename, byte retryTime = 3)
		{
			if (filename is null || !File.Exists(filename))
			{
				return;
			}

			var i = 0;
			while (true)
			{
				try
				{
					File.Delete(filename);
				}
				catch (Exception) when (i < retryTime)
				{
					++i;
					await Task.Delay(TimeSpan.FromSeconds(1));
					continue;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $@"删除 {filename} 出错");
				}

				break;
			}
		}

		public async ValueTask DisposeAsync()
		{
			CurrentSpeedSubject.OnCompleted();
			StatusSubject.OnCompleted();

			await CloseOutput(null, true);
		}
	}
}
