using BilibiliLiveRecordDownLoader.Application.Contracts.Platform;
using BilibiliLiveRecordDownLoader.Application.Http;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Channels;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomDanmuLoopTest
{
	private const long RoomId = 22907643;

	[Test]
	[Timeout(30_000)]
	public async Task ConsecutiveDanmuFailuresBackOffExponentiallyAsync(CancellationToken cancellationToken)
	{
		FakeTimeProvider time = new();
		ReconnectDelayLog log = new();
		await using ServiceProvider services = new ServiceCollection()
			.AddLogging(builder => builder.AddProvider(log))
			.AddSingleton<TimeProvider>(time)
			.AddSingleton<IAppConfigManager, FakeConfigManager>()
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddApplication()
			.AddHttpClient(HttpClientNames.BilibiliApi)
			.ConfigurePrimaryHttpMessageHandler(static () => new UnavailableHandler())
			.Services
			.BuildServiceProvider();
		RoomConfig settings = new() { RoomId = RoomId };
		await using RoomSession session = services.GetRequiredService<RoomSessionFactory>().Create(settings, static () => { });

		session.Start();
		List<TimeSpan> delays = [await log.Delays.ReadAsync(cancellationToken)];

		while (delays.Count < 4)
		{
			// 定时器可能还没注册，没等到下一次失败就再推进一次；断言只看记录下来的间隔
			time.Advance(delays[^1]);

			using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			wait.CancelAfter(TimeSpan.FromMilliseconds(200));

			try
			{
				delays.Add(await log.Delays.ReadAsync(wait.Token));
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			}
		}

		using (Assert.Multiple())
		{
			for (int i = 0; i < delays.Count; ++i)
			{
				TimeSpan expected = settings.DanmuReconnectDelay * (1 << i);
				await Assert.That(delays[i]).IsBetween(expected * ReconnectBackoff.MinJitter, expected * ReconnectBackoff.MaxJitter);
			}
		}
	}

	private sealed class UnavailableHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
		}
	}

	private sealed class ReconnectDelayLog : ILoggerProvider, ILogger
	{
		private readonly Channel<TimeSpan> _delays = Channel.CreateUnbounded<TimeSpan>();

		public ChannelReader<TimeSpan> Delays => _delays.Reader;

		public ILogger CreateLogger(string categoryName)
		{
			return this;
		}

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (eventId.Name is "LogDanmuFailed" && state is IEnumerable<KeyValuePair<string, object?>> properties && properties.FirstOrDefault(static p => p.Key == "Delay").Value is TimeSpan delay)
			{
				_delays.Writer.TryWrite(delay);
			}
		}

		public void Dispose()
		{
		}
	}
}
