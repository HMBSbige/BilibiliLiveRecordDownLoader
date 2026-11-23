namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface ISystemLanguageService
{
	// 订阅时先给出当前值；值是 BCP 47 标签，系统没有提供时为空
	IObservable<string> PreferredLanguage { get; }
}
