using BilibiliLiveRecordDownLoader.ViewModels.Platform;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

// 选择文件默认当作用户取消了，打开链接和目录什么也不做
internal sealed class PlatformStub : ILauncherService, IStoragePickerService
{
	// 参数是起始目录
	public Func<string?, string?> PickDanmakuFile { get; init; } = static _ => null;

	public ValueTask LaunchUriAsync(Uri uri)
	{
		return ValueTask.CompletedTask;
	}

	public ValueTask OpenFolderAsync(string path)
	{
		return ValueTask.CompletedTask;
	}

	public Task<string?> PickFolderAsync(string? initialDirectory = null)
	{
		return Task.FromResult<string?>(null);
	}

	public Task<string?> PickFileAsync(string? initialDirectory = null)
	{
		return Task.FromResult<string?>(null);
	}

	public Task<string?> PickVideoFileAsync(string? initialDirectory = null)
	{
		return Task.FromResult<string?>(null);
	}

	public Task<string?> PickDanmakuFileAsync(string? initialDirectory = null)
	{
		return Task.FromResult(PickDanmakuFile(initialDirectory));
	}

	public Task<string?> PickSaveVideoFileAsync(string? suggestedPath = null)
	{
		return Task.FromResult<string?>(null);
	}
}
