namespace BilibiliLiveRecordDownLoader.Application.Contracts.Storage;

public sealed record DiskSpaceInfo(long TotalBytes, long AvailableBytes)
{
	public double UsedRatio => TotalBytes > 0 ? 1 - (double)AvailableBytes / TotalBytes : 0;
}
