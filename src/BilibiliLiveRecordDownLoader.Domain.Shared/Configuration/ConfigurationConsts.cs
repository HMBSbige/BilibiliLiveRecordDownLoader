namespace BilibiliLiveRecordDownLoader.Domain.Shared.Configuration;

public static class ConfigurationConsts
{
	private const string ConfigFileName = "config.json";

	// 不加 DoNotVerify 时，Linux 上还没有 ~/.config 或 ~/.local/share 会返回空串，文件会写到当前目录
	public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppConsts.Name);

	public static string ConfigFilePath => Path.Combine(ConfigDirectory, ConfigFileName);

	public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), AppConsts.Name, "Logs");

	public static string DefaultRecordDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } videos
		? Path.Combine(videos, AppConsts.Name)
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", AppConsts.Name);
}
