using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.VideoWriters;

internal class H264Writer : IVideoWriter
{
	public int BufferSize { get; init; }
	public bool IsAsync { get; init; }
	public string Path { get; }

	private static ReadOnlySpan<byte> StartCode => new byte[] { 0, 0, 0, 1 };
	private readonly FileStream _fs;
	private int _nalLengthSize;

	public H264Writer(string path, bool isAsync, int bufferSize)
	{
		Path = path;
		IsAsync = isAsync;
		BufferSize = bufferSize;
		_fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, IsAsync);
	}

	public void Write(Memory<byte> buffer, uint timestamp, FrameType type)
	{
		if (buffer.Length < 4)
		{
			return;
		}

		if (buffer.Span[0] == 0) // Headers
		{
			if (buffer.Length < 10)
			{
				return;
			}

			var offset = 8;
			_nalLengthSize = (buffer.Span[offset++] & 0x03) + 1;
			var spsCount = buffer.Span[offset++] & 0x1F;
			var ppsCount = -1;

			while (offset <= buffer.Length - 2)
			{
				if (spsCount == 0 && ppsCount == -1)
				{
					ppsCount = buffer.Span[offset++];
					continue;
				}

				if (spsCount > 0)
				{
					--spsCount;
				}
				else if (ppsCount > 0)
				{
					--ppsCount;
				}
				else
				{
					break;
				}

				var len = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..].Span);
				offset += 2;
				if (offset + len > buffer.Length)
				{
					break;
				}

				_fs.Write(StartCode);
				_fs.Write(buffer.Slice(offset, len).Span);
				offset += len;
			}
		}
		else // Video data
		{
			var offset = 4;

			if (_nalLengthSize != 2)
			{
				_nalLengthSize = 4;
			}

			while (offset <= buffer.Length - _nalLengthSize)
			{
				var len = _nalLengthSize == 2
					? BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..].Span)
					: (int)BinaryPrimitives.ReadUInt32BigEndian(buffer[offset..].Span);
				offset += _nalLengthSize;

				if (offset + len > buffer.Length)
				{
					break;
				}

				_fs.Write(StartCode);
				_fs.Write(buffer.Slice(offset, len).Span);
				offset += len;
			}
		}
	}

	public async ValueTask FinishAsync(FractionUInt32 averageFrameRate)
	{
		await _fs.DisposeAsync();
	}
}
