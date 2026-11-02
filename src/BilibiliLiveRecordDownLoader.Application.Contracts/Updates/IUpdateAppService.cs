namespace BilibiliLiveRecordDownLoader.Application.Contracts.Updates;

public interface IUpdateAppService
{
	string CurrentVersion { get; }

	Task<UpdateCheckResult> CheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default);
}

public sealed record UpdateCheckResult(bool HasUpdate, string? LatestVersion);
