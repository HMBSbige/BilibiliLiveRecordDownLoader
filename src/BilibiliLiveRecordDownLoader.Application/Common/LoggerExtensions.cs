namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static class LoggerExtensions
{
	extension(ILogger logger)
	{
		public IDisposable? BeginRoomScope(long roomId)
		{
			return logger.BeginScope(CreateRoomScope(roomId));
		}

		// 房间的方法也会被添加、刷新房间等外部调用直接调用，只靠调用链上的作用域会漏掉房间号
		public ILogger WithRoomScope(long roomId)
		{
			return new RoomScopedLogger(logger, CreateRoomScope(roomId));
		}
	}

	// 每条日志都要读一遍作用域，Serilog 对 Dictionary<string, object> 有不经接口枚举的专门路径
	private static Dictionary<string, object> CreateRoomScope(long roomId)
	{
		return new(StringComparer.Ordinal) { [LogProperties.RoomId] = roomId };
	}

	private sealed class RoomScopedLogger(ILogger inner, Dictionary<string, object> roomScope) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return inner.BeginScope(state);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return inner.IsEnabled(logLevel);
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			using (inner.BeginScope(roomScope))
			{
				inner.Log(logLevel, eventId, state, exception, formatter);
			}
		}
	}
}
