namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class LauncherService(ApplicationShell shell) : ILauncherService
{
	public async ValueTask LaunchUriAsync(Uri uri)
	{
		if (!await shell.Window.Launcher.LaunchUriAsync(uri))
		{
			throw new InvalidOperationException(AppLanguage.Current.OpenFailed.ToString(uri.AbsoluteUri));
		}
	}

	public async ValueTask OpenFolderAsync(string path)
	{
		DirectoryInfo directory = Directory.CreateDirectory(path);

		if (!await shell.Window.Launcher.LaunchDirectoryInfoAsync(directory))
		{
			throw new InvalidOperationException(AppLanguage.Current.OpenFailed.ToString(directory.FullName));
		}
	}
}
