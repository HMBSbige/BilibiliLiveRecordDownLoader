namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class ConfigurationConsts
{
	private const string ConfigFileName = "config.json";

	public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConsts.Name);

	public static string ConfigFilePath => Path.Combine(ConfigDirectory, ConfigFileName);

	public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConsts.Name, "Logs");

	// 视频目录不存在时 MyVideos 为空
	public static string DefaultRecordDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } videos
		? Path.Combine(videos, AppConsts.Name)
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", AppConsts.Name);
}
