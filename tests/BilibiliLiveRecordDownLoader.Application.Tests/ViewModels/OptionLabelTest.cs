using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using BilibiliLiveRecordDownLoader.ViewModels.Common;
using BilibiliLiveRecordDownLoader.ViewModels.Logs;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

// 下拉框收起时，读屏念的是选中数据项的 ToString
public sealed class OptionLabelTest
{
	public static IEnumerable<Func<(object Option, string Label)>> Options()
	{
		yield return static () => (ChoiceOption.Inherit, AppLanguage.Current.Inherit);
		yield return static () => (new ChoiceOption(false), AppLanguage.Current.Disabled);
		yield return static () => (new ChoiceOption(RecordingSplitMode.Duration), AppLanguage.Current.SplitByDuration);
		yield return static () => (new LanguageOptionViewModel(string.Empty, string.Empty), AppLanguage.Current.FollowSystem);
		yield return static () => (new LanguageOptionViewModel("en", "English"), "English");
		// 日志筛选的空值是全部级别，不是使用全局设置
		yield return static () => (LogLevelOption.All, AppLanguage.Current.AllLevels);
		yield return static () => (new LogLevelOption(LogLevel.Warning), AppLanguage.Current.LogLevelWarning);
	}

	[Test]
	[MethodDataSource(nameof(Options))]
	public async Task OptionReadsAsItsLabelAsync(object option, string label)
	{
		await Assert.That(option.ToString()).IsEqualTo(label);
	}
}
