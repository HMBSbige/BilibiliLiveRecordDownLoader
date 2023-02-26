using Serilog.Events;

namespace BilibiliLiveRecordDownLoader.Models;

public record LogModel
{
	public DateTimeOffset Timestamp { get; set; }

	public LogEventLevel Level { get; set; }

	public long? RoomId { get; set; }

	public string? Message { get; set; }
}
