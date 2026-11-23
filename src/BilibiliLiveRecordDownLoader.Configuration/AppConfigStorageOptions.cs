namespace BilibiliLiveRecordDownLoader.Configuration;

public sealed class AppConfigStorageOptions
{
	public string FilePath { get; set; } = ConfigurationConsts.ConfigFilePath;

	// 3.x 的 WPF 版把配置存在程序旁边，只有 Windows 版
	public string? LegacyFilePath { get; set; } = OperatingSystem.IsWindows() ? Path.Combine(AppContext.BaseDirectory, "BilibiliLiveRecordDownLoader.json") : null;

	public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}
