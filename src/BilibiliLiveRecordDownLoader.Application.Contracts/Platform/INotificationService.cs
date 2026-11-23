namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface INotificationService
{
	void Show(string title, string message, AppNotificationType type = AppNotificationType.Information, Uri? link = null);
}
