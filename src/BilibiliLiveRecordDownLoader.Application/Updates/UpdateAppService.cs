using NuGet.Versioning;
using UpdateChecker;

namespace BilibiliLiveRecordDownLoader.Application.Updates;

internal sealed partial class UpdateAppService(IHttpClientFactory httpClientFactory, ILogger<UpdateAppService> logger) : IUpdateAppService
{
	public string CurrentVersion { get; } = Normalize(ThisAssembly.Info.InformationalVersion) ?? string.Empty;

	public async Task<UpdateCheckResult> CheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default)
	{
		HttpClient httpClient = httpClientFactory.CreateClient(HttpClientNames.Default);
		httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(AppConsts.Name);

		GitHubReleasesUpdateChecker checker = new
		(
			new GitHubReleasesUpdateCheckerOptions
			{
				Owner = AppConsts.Author,
				Repo = AppConsts.Repository,
				IsPreRelease = includePreRelease,
				CurrentVersion = CurrentVersion,
				VersionScheme = new NuGetVersionScheme()
			}
		);

		bool hasUpdate = await RequestTimeout.ReportAsync(checker.CheckAsync(httpClient, cancellationToken).AsTask());

		return new UpdateCheckResult(hasUpdate, checker.LatestVersion);
	}

	public async Task<UpdateCheckResult?> TryCheckForUpdateAsync(bool includePreRelease, CancellationToken cancellationToken = default)
	{
		try
		{
			return await CheckForUpdateAsync(includePreRelease, cancellationToken);
		}
		catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
		{
			LogCheckFailed(logger, exception);
			return null;
		}
	}

	private static string? Normalize(string value)
	{
		return NuGetVersion.TryParse(value, out NuGetVersion? version) ? version.ToNormalizedString() : null;
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check for updates")]
	private static partial void LogCheckFailed(ILogger logger, Exception exception);

	private sealed class NuGetVersionScheme : IVersionScheme
	{
		public bool TryParse(string value, out string version)
		{
			string? normalized = Normalize(value);
			version = normalized ?? string.Empty;
			return normalized is not null;
		}

		public int Compare(string x, string y)
		{
			return NuGetVersion.Parse(x).CompareTo(NuGetVersion.Parse(y));
		}
	}
}
