using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Http;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Web;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomStreamRequestTest
{
	// 沿用的候选流是按旧画质请求的，按新画质判断受限会误报；失败过的线路仍然跳过
	[Test]
	public async Task QualityChangedBetweenAttemptsRequestsTheCandidatesAgainAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		string? original = RequestedQuality(loop);
		loop.Settings = loop.Settings with { Quality = StreamQuality.BluRay };
		await loop.RecordForAsync(first, TimeSpan.FromSeconds(1));
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
			// 没开真原画时按所选的原画请求
			await Assert.That((original, RequestedQuality(loop))).IsEqualTo(("10000", "400"));
			await Assert.That(next.Host).IsEqualTo("b.test");
		}
	}

	[Test]
	public async Task RecordingLogShowsTheStreamPathWithoutTheQueryAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted.WaitAsync(cancellationToken);
		string logged = await Assert.That(loop.RecorderLogs).HasSingleItem(static message => message.StartsWith("Recording ", StringComparison.Ordinal));

		// B 站真实地址的查询参数里有用户的 uid
		await Assert.That(logged).Contains($"{request.Host}/live/avc.flv ").And.DoesNotContain("sign=");
	}

	// FFmpeg 的最后几行输出是唯一的诊断信息，要照样展示，但其中地址的查询串里有用户的 uid 和签名
	[Test]
	public async Task FFmpegFailureShowsItsLastOutputInTheAppLanguageWithoutTheQueryAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg);
		loop.FailFFmpeg("[http @ 0] Opening 'https://a.test/live/avc.flv?sign=1' for reading");
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsEqualTo(AppLanguage.Current.RecordingEndpointFailed.ToString("Flv/Avc", "a.test", AppLanguage.Current.FFmpegFailed.ToString(1, "[http @ 0] Opening 'https://a.test/live/avc.flv?…' for reading")));
			await Assert.That(loop.Logs.Where(static log => $"{log.Message} {log.Exception}".Contains("sign=", StringComparison.Ordinal))).IsEmpty();
		}
	}

	[Test]
	public async Task ConnectionTimeoutIsShownInTheAppLanguageAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		for (int i = 0; i < 3; ++i)
		{
			_ = await loop.NextStreamAsync();
			loop.Time.Advance(loop.Settings.StreamConnectTimeout);
		}

		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).IsEqualTo(AppLanguage.Current.RecordingStreamsUnavailable.ToString(AppLanguage.Current.RecordingEndpointFailed.ToString("Flv/Av1", "c.test", AppLanguage.Current.RecordingConnectionTimedOut)));
			await Assert.That(loop.Logs).Contains(static log => log.Exception is TimeoutException);
		}
	}

	// 请求候选流超时或被风控拦截时，异常原文是 HttpClient 的英文说明
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FailedCandidateRequestIsShownInTheAppLanguageAsync(bool isBlocked, CancellationToken cancellationToken)
	{
		await using ServiceProvider services = new ServiceCollection()
			.AddLogging()
			.AddSingleton<TimeProvider>(new FakeTimeProvider())
			.AddSingleton<IAppConfigManager, FakeConfigManager>()
			.AddSingleton<INotificationService, RecordingNotifications>()
			.AddApplication()
			.AddHttpClient(HttpClientNames.BilibiliApi)
			.ConfigurePrimaryHttpMessageHandler(() => new FailingHandler(isBlocked))
			.Services
			.BuildServiceProvider();
		await using RoomSession session = services.GetRequiredService<RoomSessionFactory>().Create(new RoomConfig { RoomId = RecordingLoopHarness.RoomId }, static () => { });

		session.Apply(new LiveRoomInfo(RecordingLoopHarness.RoomId, 0, 1, "streamer", "title", LiveStatus.Live));
		await TestWait.UntilAsync(() => session.GetSnapshot().LastError is not null, "the failure is shown", cancellationToken);

		await Assert.That(session.GetSnapshot().LastError).IsEqualTo(isBlocked ? AppLanguage.Current.RequestRateLimited : AppLanguage.Current.RequestTimedOut);
	}

	private static string? RequestedQuality(RecordingLoopHarness loop)
	{
		return HttpUtility.ParseQueryString(loop.PlayInfoQuery ?? string.Empty)["qn"];
	}

	// HttpClient 超时抛的是带着 TimeoutException 的 TaskCanceledException
	private sealed class FailingHandler(bool isBlocked) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (!isBlocked)
			{
				return Task.FromException<HttpResponseMessage>(new TaskCanceledException(null, new TimeoutException()));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed));
		}
	}
}
