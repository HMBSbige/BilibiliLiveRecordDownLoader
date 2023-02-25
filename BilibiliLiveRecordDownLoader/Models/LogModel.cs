using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog.Events;

namespace BilibiliLiveRecordDownLoader.Models;

public class LogModel : ReactiveObject
{
	[Reactive]
	public DateTimeOffset Timestamp { get; set; }

	[Reactive]
	public LogEventLevel Level { get; set; }

	[Reactive]
	public long? RoomId { get; set; }

	[Reactive]
	public string? Message { get; set; }
}
