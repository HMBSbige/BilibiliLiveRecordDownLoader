namespace BilibiliLiveRecordDownLoader.Application.Contracts.Storage;

public sealed record DiskSpaceInfo(long TotalBytes, long AvailableBytes)
{
	public const long LowThreshold = 1L << 30;

	public double UsedRatio => TotalBytes > 0 ? 1 - (double)AvailableBytes / TotalBytes : 0;

	public bool IsLow => AvailableBytes < LowThreshold;
}
