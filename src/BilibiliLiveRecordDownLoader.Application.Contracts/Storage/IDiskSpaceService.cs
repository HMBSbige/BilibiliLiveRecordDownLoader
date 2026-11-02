namespace BilibiliLiveRecordDownLoader.Application.Contracts.Storage;

public interface IDiskSpaceService
{
	DiskSpaceInfo? GetDiskSpace(string path);
}
