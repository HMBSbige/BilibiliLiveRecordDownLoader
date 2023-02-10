using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Utils;
using System.Buffers;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.AudioWrites;

internal class AACWriter : IAudioWriter
{
	public int BufferSize { get; init; }
	public bool IsAsync { get; init; }
	public string Path { get; }

	private readonly FileStream _fs;
	private int _aacProfile;
	private int _sampleRateIndex;
	private int _channelConfig;

	public AACWriter(string path, bool isAsync, int bufferSize)
	{
		Path = path;
		IsAsync = isAsync;
		BufferSize = bufferSize;
		_fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, IsAsync);
	}

	public void Write(Memory<byte> buffer, uint timestamp)
	{
		if (buffer.Length < 1)
		{
			return;
		}

		if (buffer.Span[0] == 0) // Header
		{
			if (buffer.Length < 3)
			{
				return;
			}

			var bits = (ulong)BinaryPrimitives.ReadUInt16BigEndian(buffer[1..].Span) << 48;

			_aacProfile = BitOperations.Read(ref bits, 5) - 1;
			_sampleRateIndex = BitOperations.Read(ref bits, 4);
			_channelConfig = BitOperations.Read(ref bits, 4);

			if (_aacProfile == 4) // HE-AAC
			{
				_aacProfile = 1; // Uses LC profile + SBR
			}

			if (_aacProfile is < 0 or > 3)
			{
				throw new Exception(@"Unsupported AAC profile.");
			}

			if (_sampleRateIndex > 12)
			{
				throw new Exception(@"Invalid AAC sample rate index.");
			}

			if (_channelConfig > 6)
			{
				throw new Exception(@"Invalid AAC channel configuration.");
			}
		}
		else // Audio data
		{
			var dataSize = buffer.Length - 1;
			ulong bits = 0;

			BitOperations.Write(ref bits, 12, 0xFFF);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 2, 0);
			BitOperations.Write(ref bits, 1, 1);
			BitOperations.Write(ref bits, 2, _aacProfile);
			BitOperations.Write(ref bits, 4, _sampleRateIndex);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 3, _channelConfig);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 1, 0);
			BitOperations.Write(ref bits, 13, 7 + dataSize);
			BitOperations.Write(ref bits, 11, 0x7FF);
			BitOperations.Write(ref bits, 2, 0);

			using (var memoryBuffer = MemoryPool<byte>.Shared.Rent(sizeof(ulong)))
			{
				BinaryPrimitives.WriteUInt64BigEndian(memoryBuffer.Memory.Span, bits);
				_fs.Write(memoryBuffer.Memory.Slice(1, 7).Span);
			}
			_fs.Write(buffer.Slice(1, dataSize).Span);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _fs.DisposeAsync();
	}
}
