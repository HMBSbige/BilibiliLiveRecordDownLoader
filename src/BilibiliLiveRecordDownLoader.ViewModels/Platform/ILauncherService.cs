namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface ILauncherService
{
	ValueTask LaunchUriAsync(Uri uri);

	ValueTask OpenFolderAsync(string path);
}
