namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface ILauncherService
{
	ValueTask LaunchUriAsync(Uri uri);

	// 目录不存在时先创建
	ValueTask OpenFolderAsync(string path);
}
