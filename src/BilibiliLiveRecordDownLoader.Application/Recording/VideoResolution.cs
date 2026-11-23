namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 显示尺寸，已去掉编码时补齐的裁剪区域
internal readonly record struct VideoResolution(int Width, int Height)
{
	// AV1 最高级别的宽度上限，超出当作读错了
	private const int MaxDimension = 16384;

	public static VideoResolution? Create(long width, long height)
	{
		return width is > 0 and <= MaxDimension && height is > 0 and <= MaxDimension ? new VideoResolution((int)width, (int)height) : null;
	}
}
