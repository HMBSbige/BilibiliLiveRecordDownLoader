using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

// 不用 ChoiceOption：它的空值念作“使用全局设置”，这里的空值是不按级别筛选
public sealed record LogLevelOption(LogLevel? Minimum)
{
	public static LogLevelOption All { get; } = new((LogLevel?)null);

	public override string ToString()
	{
		return Minimum is { } level ? Labels.Of(level) : AppLanguage.Current.AllLevels;
	}
}
