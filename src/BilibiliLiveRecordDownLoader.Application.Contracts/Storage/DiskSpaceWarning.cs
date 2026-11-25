namespace BilibiliLiveRecordDownLoader.Application.Contracts.Storage;

public static class DiskSpaceWarning
{
	public const long Threshold = 1L << 30;

	public static bool IsLow(DiskSpaceInfo? diskSpace)
	{
		return diskSpace?.AvailableBytes < Threshold;
	}
}
