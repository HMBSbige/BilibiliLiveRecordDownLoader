namespace BilibiliLiveRecordDownLoader.Configuration;

public sealed class AppConfigStorageOptions
{
	public string FilePath { get; set; } = ConfigurationConsts.ConfigFilePath;

	public string? LegacyFilePath { get; set; } = OperatingSystem.IsWindows() ? Path.Combine(AppContext.BaseDirectory, "BilibiliLiveRecordDownLoader.json") : null;

	public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}
