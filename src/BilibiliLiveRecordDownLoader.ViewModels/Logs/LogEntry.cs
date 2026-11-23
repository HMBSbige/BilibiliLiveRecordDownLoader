using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

public sealed record LogEntry(long Sequence, DateTimeOffset Timestamp, LogLevel Level, long? RoomId, string Message);
