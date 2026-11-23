namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface ISystemLanguageService
{
	// 订阅时先给出当前值，系统首选语言改变时再给出新值；值是 BCP 47 标签，例如 zh-Hans-CN，系统没有提供时为空
	IObservable<string> PreferredLanguage { get; }
}
