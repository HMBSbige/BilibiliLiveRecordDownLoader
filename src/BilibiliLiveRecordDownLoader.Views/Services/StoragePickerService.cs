namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class StoragePickerService(ApplicationShell shell) : IStoragePickerService
{
	private IStorageProvider StorageProvider => shell.Window.StorageProvider;

	public async Task<string?> PickFolderAsync(string? initialDirectory = null)
	{
		IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync
		(
			new FolderPickerOpenOptions
			{
				SuggestedStartLocation = await TryGetFolderAsync(initialDirectory)
			}
		);

		return folders is [var folder, ..] ? folder.TryGetLocalPath() : null;
	}

	public Task<string?> PickFileAsync(string? initialDirectory = null)
	{
		return PickOpenFileAsync(initialDirectory, [AllFileType]);
	}

	public Task<string?> PickVideoFileAsync(string? initialDirectory = null)
	{
		return PickOpenFileAsync(initialDirectory, [VideoFileType, AllFileType]);
	}

	public Task<string?> PickDanmakuFileAsync(string? initialDirectory = null)
	{
		return PickOpenFileAsync(initialDirectory, [DanmakuFileType, AllFileType]);
	}

	public async Task<string?> PickSaveVideoFileAsync(string? suggestedPath = null)
	{
		bool hasSuggestion = !string.IsNullOrWhiteSpace(suggestedPath);

		IStorageFile? file = await StorageProvider.SaveFilePickerAsync
		(
			new FilePickerSaveOptions
			{
				SuggestedStartLocation = await TryGetFolderAsync(Path.GetDirectoryName(suggestedPath)),
				SuggestedFileName = hasSuggestion ? Path.GetFileName(suggestedPath) : null,
				DefaultExtension = hasSuggestion ? Path.GetExtension(suggestedPath) : ".mp4",
				FileTypeChoices = [VideoFileType, AllFileType]
			}
		);

		return file?.TryGetLocalPath();
	}

	private static FilePickerFileType VideoFileType => new(AppLanguage.Current.VideoFiles) { Patterns = ["*.flv", "*.mp4", "*.ts", "*.mkv", "*.m4s"] };

	private static FilePickerFileType DanmakuFileType => new(AppLanguage.Current.DanmakuFiles) { Patterns = ["*.xml"] };

	// "*" 才能匹配 Unix 上没有扩展名的 ffmpeg
	private static FilePickerFileType AllFileType => new(AppLanguage.Current.AllFiles) { Patterns = ["*"] };

	private async Task<string?> PickOpenFileAsync(string? initialDirectory, IReadOnlyList<FilePickerFileType> fileTypes)
	{
		IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync
		(
			new FilePickerOpenOptions
			{
				SuggestedStartLocation = await TryGetFolderAsync(initialDirectory),
				FileTypeFilter = fileTypes
			}
		);

		return files is [var file, ..] ? file.TryGetLocalPath() : null;
	}

	private async Task<IStorageFolder?> TryGetFolderAsync(string? path)
	{
		return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) ? null : await StorageProvider.TryGetFolderFromPathAsync(path);
	}
}
