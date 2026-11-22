namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface INotificationService
{
	// 指定 link 时点击通知会打开该链接
	void Show(string title, string message, AppNotificationType type = AppNotificationType.Information, Uri? link = null);
}
