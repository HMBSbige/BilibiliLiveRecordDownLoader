namespace BilibiliLiveRecordDownLoader.Application.Contracts.Updates;

public interface IUpdateAppService
{
	string CurrentVersion { get; }

	Task<UpdateCheckResult> CheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default);

	// 失败只记日志并返回 null，用于没有用户操作的后台检查
	Task<UpdateCheckResult?> TryCheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default);
}

public sealed record UpdateCheckResult(bool HasUpdate, string? LatestVersion);
