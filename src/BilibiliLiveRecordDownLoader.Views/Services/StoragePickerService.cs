namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class StoragePickerService(ApplicationShell shell) : IStoragePickerService
{
	private static readonly string[] VideoPatterns = ["*.flv", "*.mp4", "*.ts", "*.mkv", "*.m4s"];

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

	// Linux 的对话框给没写扩展名的文件名补上所选类型里第一个具体的模式，所以目标扩展名要排在最前；
	// “所有文件”写成 *.*，补扩展名时会跳过它，改用默认扩展名
	public async Task<string?> PickSaveVideoFileAsync(string? suggestedPath = null)
	{
		string extension = Path.GetExtension(suggestedPath) is { Length: > 0 } suggested ? suggested : ".mp4";
		string pattern = $"*{extension}";

		IStorageFile? file = await StorageProvider.SaveFilePickerAsync
		(
			new FilePickerSaveOptions
			{
				SuggestedStartLocation = await TryGetFolderAsync(Path.GetDirectoryName(suggestedPath)),
				SuggestedFileName = string.IsNullOrWhiteSpace(suggestedPath) ? null : Path.GetFileName(suggestedPath),
				// Windows 的 IFileDialog.SetDefaultExtension 要求不带点
				DefaultExtension = extension.TrimStart('.'),
				FileTypeChoices =
				[
					new FilePickerFileType(AppLanguage.Current.VideoFiles) { Patterns = [pattern, .. VideoPatterns.Where(p => !p.Equals(pattern, StringComparison.OrdinalIgnoreCase))] },
					new FilePickerFileType(AppLanguage.Current.AllFiles) { Patterns = ["*.*"] }
				]
			}
		);

		return file?.TryGetLocalPath();
	}

	private static FilePickerFileType VideoFileType => new(AppLanguage.Current.VideoFiles) { Patterns = VideoPatterns };

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
