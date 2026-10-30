namespace BilibiliLiveRecordDownLoader.Configuration;

public sealed class AppConfigStorageOptions
{
	public string FilePath { get; set; } = ConfigurationConsts.ConfigFilePath;

	public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(300);
}
