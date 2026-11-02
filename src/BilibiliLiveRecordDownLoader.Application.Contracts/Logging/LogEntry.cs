namespace BilibiliLiveRecordDownLoader.Application.Contracts.Logging;

public sealed record LogEntry(long Sequence, DateTimeOffset Timestamp, LogLevel Level, long? RoomId, string Message);
