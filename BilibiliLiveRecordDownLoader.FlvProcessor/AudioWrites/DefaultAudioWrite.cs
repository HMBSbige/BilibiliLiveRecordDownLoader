using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.AudioWrites;

internal class DefaultAudioWrite : IAudioWriter
{
	public int BufferSize { get; init; }
	public bool IsAsync { get; init; }
	public string Path => string.Empty;
	public void Write(Memory<byte> buffer, uint timestamp) { }
	public ValueTask DisposeAsync()
	{
		return default;
	}
}
