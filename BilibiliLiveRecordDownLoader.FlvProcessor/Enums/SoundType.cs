namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums
{
	/// <summary>
	/// 音频类型，1 bit
	/// 对于AAC总是 Stereo
	/// </summary>
	public enum SoundType : byte
	{
		Mono = 0,
		Stereo = 1
	}
}
