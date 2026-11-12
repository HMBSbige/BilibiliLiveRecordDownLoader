using NuGet.Versioning;
using UpdateChecker;

namespace BilibiliLiveRecordDownLoader.Application.Updates;

internal sealed class UpdateAppService(IHttpClientFactory httpClientFactory) : IUpdateAppService
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

		bool hasUpdate = await checker.CheckAsync(httpClient, cancellationToken);

		return new UpdateCheckResult(hasUpdate, checker.LatestVersion);
	}

	private static string? Normalize(string value)
	{
		return NuGetVersion.TryParse(value, out NuGetVersion? version) ? version.ToNormalizedString() : null;
	}

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
