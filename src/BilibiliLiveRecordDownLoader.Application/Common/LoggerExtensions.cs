namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class LoggerExtensions
{
	extension(ILogger logger)
	{
		public IDisposable? BeginRoomScope(long roomId)
		{
			return logger.BeginScope(new Dictionary<string, object> { [LogProperties.RoomId] = roomId });
		}
	}
}
