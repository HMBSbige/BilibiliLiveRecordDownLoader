namespace BilibiliLiveRecordDownLoader.Views;

public sealed record AppStartupOptions(bool StartMinimized)
{
	public const string MinimizedArgument = "--minimized";

	public static AppStartupOptions Parse(IReadOnlyCollection<string> args)
	{
		return new AppStartupOptions(args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase));
	}
}
