namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface IStoragePickerService
{
	Task<string?> PickFolderAsync(string? initialDirectory = null);

	Task<string?> PickFileAsync(string? initialDirectory = null);

	Task<string?> PickVideoFileAsync(string? initialDirectory = null);

	Task<string?> PickDanmakuFileAsync(string? initialDirectory = null);

	Task<string?> PickSaveVideoFileAsync(string? suggestedPath = null);
}
