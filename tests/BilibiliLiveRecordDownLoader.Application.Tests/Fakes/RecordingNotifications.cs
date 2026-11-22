using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed record ShownNotification(string Title, string Message, AppNotificationType Type, Uri? Link);

internal sealed class RecordingNotifications : INotificationService
{
	public ConcurrentQueue<ShownNotification> Shown { get; } = new();

	public void Show(string title, string message, AppNotificationType type = AppNotificationType.Information, Uri? link = null)
	{
		Shown.Enqueue(new ShownNotification(title, message, type, link));
	}
}
