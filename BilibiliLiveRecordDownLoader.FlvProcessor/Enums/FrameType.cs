namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums
{
	public enum FrameType : byte // 其实是半个字节
	{
		/// <summary>
		/// 关键帧 for AVC, a seekable frame
		/// </summary>
		KeyFrame = 1,

		/// <summary>
		/// for AVC, a non-seekable frame
		/// </summary>
		InterFrame = 2,

		/// <summary>
		/// H.263 only
		/// </summary>
		DisposableInterFrame = 3,

		/// <summary>
		/// reserved for server use only
		/// </summary>
		GeneratedKeyFrame = 4,

		/// <summary>
		/// video info/command frame
		/// </summary>
		CommandFrame = 5
	}
}
