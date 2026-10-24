namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class ConfigurationConsts
{
	private const string ConfigFileName = "config.json";

	public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConsts.Name);

	public static string ConfigFilePath => Path.Combine(ConfigDirectory, ConfigFileName);

	public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConsts.Name, "Logs");

	public static string DefaultRecordDirectory
	{
		get
		{
			string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

			// XDG 用户目录未配置时 MyVideos 为空
			return string.IsNullOrEmpty(videos)
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", AppConsts.Name)
				: Path.Combine(videos, AppConsts.Name);
		}
	}
}
