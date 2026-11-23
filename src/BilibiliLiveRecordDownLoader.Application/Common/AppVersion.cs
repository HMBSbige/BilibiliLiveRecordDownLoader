using NuGet.Versioning;

namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class AppVersion
{
	public static string Current { get; } = Normalize(ThisAssembly.Info.InformationalVersion) ?? string.Empty;

	public static string? Normalize(string value)
	{
		return NuGetVersion.TryParse(value, out NuGetVersion? version) ? version.ToNormalizedString() : null;
	}
}
