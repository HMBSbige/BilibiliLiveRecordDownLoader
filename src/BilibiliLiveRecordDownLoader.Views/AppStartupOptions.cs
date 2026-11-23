namespace BilibiliLiveRecordDownLoader.Views;

public sealed record AppStartupOptions(bool StartMinimized)
{
	public const string MinimizedArgument = "--minimized";

	// 旧版的开机启动登记用它，原地升级后这条登记启动的就是当前程序
	internal const string LegacySilentArgument = "-silent";

	public static AppStartupOptions Parse(IReadOnlyCollection<string> args)
	{
		return new AppStartupOptions(args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase) || args.Contains(LegacySilentArgument, StringComparer.OrdinalIgnoreCase));
	}
}
