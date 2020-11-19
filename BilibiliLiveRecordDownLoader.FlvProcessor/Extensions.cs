using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

namespace BilibiliLiveRecordDownLoader.FlvProcessor
{
	public static class Extensions
	{
		public static HeaderFlags ToFlvHeaderFlags(this byte b)
		{
			return (HeaderFlags)b & HeaderFlags.VideoAndAudio;
		}

		public static bool IsFrameType(this byte b)
		{
			var frameType = (FrameType)(b >> 4);
			return frameType is
				FrameType.KeyFrame or
				FrameType.InterFrame or
				FrameType.DisposableInterFrame or
				FrameType.GeneratedKeyFrame;
		}
	}
}
