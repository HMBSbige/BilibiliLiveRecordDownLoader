using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using System.Runtime.CompilerServices;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Utils
{
	public static class Extensions
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static HeaderFlags ToFlvHeaderFlags(this byte b)
		{
			return (HeaderFlags)b & HeaderFlags.VideoAndAudio;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static CodecID ToCodecID(this byte b)
		{
			return (CodecID)(b & 0x0F);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static SoundFormat ToSoundFormat(this byte b)
		{
			return (SoundFormat)(b >> 4);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static SoundRate ToSoundRate(this byte b)
		{
			return (SoundRate)((b >> 2) & 0b0011);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static SoundSize ToSoundSize(this byte b)
		{
			return (SoundSize)((b >> 1) & 0b001);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static SoundType ToSoundType(this byte b)
		{
			return (SoundType)(b & 0b0001);
		}
	}
}
