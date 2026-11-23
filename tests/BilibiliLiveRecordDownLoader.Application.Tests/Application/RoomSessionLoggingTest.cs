using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Contracts.Logging;
using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomSessionLoggingTest
{
	private const long RoomId = 22907643;

	[Test]
	public async Task RoomInfoAppliedByAnOutsideCallerIsLoggedWithTheRoomIdAsync()
	{
		CapturingLoggerProvider logs = new();
		await using ServiceProvider services = new ServiceCollection()
			.AddLogging(builder => builder.AddProvider(logs))
			.AddSingleton<IAppConfigManager, FakeConfigManager>()
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddApplication()
			.BuildServiceProvider();
		await using RoomSession session = services.GetRequiredService<RoomSessionFactory>().Create(new RoomConfig { RoomId = RoomId }, static () => { });

		session.Apply(new LiveRoomInfo(RoomId, 0, 1, "streamer", "title", LiveStatus.Idle));

		await Assert.That(logs.Entries).Count().IsEqualTo(2).And.All(static entry => entry.RoomId is RoomId);
	}

	private sealed record CapturedEntry(string Message, object? RoomId);

	private sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope, ILogger
	{
		private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

		public ConcurrentQueue<CapturedEntry> Entries { get; } = new();

		public ILogger CreateLogger(string categoryName)
		{
			return this;
		}

		public void SetScopeProvider(IExternalScopeProvider scopeProvider)
		{
			_scopes = scopeProvider;
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return _scopes.Push(state);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			object? roomId = null;
			_scopes.ForEachScope((scope, _) => roomId ??= scope is IEnumerable<KeyValuePair<string, object>> properties ? properties.FirstOrDefault(p => p.Key == LogProperties.RoomId).Value : null, (object?)null);
			Entries.Enqueue(new CapturedEntry(formatter(state, exception), roomId));
		}

		public void Dispose()
		{
		}
	}
}
