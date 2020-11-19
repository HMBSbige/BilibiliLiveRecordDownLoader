using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

namespace BilibiliLiveRecordDownLoader.FlvProcessor
{
	public static class Extensions
	{
		public static HeaderFlags ToFlvHeaderFlags(this byte b)
		{
			return (HeaderFlags)b & HeaderFlags.VideoAndAudio;
		}

		public static FrameType ToFrameType(this byte b)
		{
			return (FrameType)(b >> 4);
		}

		public static bool IsFrameType(this byte b)
		{
			var frameType = b.ToFrameType();
			return frameType is
				FrameType.KeyFrame or
				FrameType.InterFrame or
				FrameType.DisposableInterFrame or
				FrameType.GeneratedKeyFrame;
		}
	}
}
