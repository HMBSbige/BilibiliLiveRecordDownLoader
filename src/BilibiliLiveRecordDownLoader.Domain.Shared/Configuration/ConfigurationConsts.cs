namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class ConfigurationConsts
{
	private const string ConfigFileName = "config.json";

	// 配置和日志目录都不检查是否存在，写入前会创建；检查的话，Linux 上还没有 ~/.config 或 ~/.local/share 时返回空串，文件会写到当前目录
	public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppConsts.Name);

	public static string ConfigFilePath => Path.Combine(ConfigDirectory, ConfigFileName);

	public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppConsts.Name, "Logs");

	// 视频目录不存在时 MyVideos 为空
	public static string DefaultRecordDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } videos
		? Path.Combine(videos, AppConsts.Name)
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", AppConsts.Name);
}
