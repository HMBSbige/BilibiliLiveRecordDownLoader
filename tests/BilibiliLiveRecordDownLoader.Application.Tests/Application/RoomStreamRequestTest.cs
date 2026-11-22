using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Web;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomStreamRequestTest
{
	[Test]
	[Timeout(30_000)]
	[Arguments(false, "10000")]
	[Arguments(true, "25000")]
	public async Task StreamCandidatesAreRequestedAtTheTrueOriginalOnlyWhenTurnedOnAsync(bool requestTrueOriginal, string expected, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Config.RequestTrueOriginal = requestTrueOriginal;
		loop.GoLive();

		_ = await loop.NextStreamAsync();

		await Assert.That(HttpUtility.ParseQueryString(loop.PlayInfoQuery ?? string.Empty)["qn"]).IsEqualTo(expected);
	}

	// 沿用的候选流是按旧画质请求的，按新画质判断受限会误报；失败过的线路仍然跳过
	[Test]
	[Timeout(30_000)]
	public async Task QualityChangedBetweenAttemptsRequestsTheCandidatesAgainAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest first = await loop.NextStreamAsync();
		loop.Session.UpdateSettings(loop.Settings with { Quality = StreamQuality.BluRay });
		await loop.RecordForAsync(first, TimeSpan.FromSeconds(1));
		StreamRequest next = await loop.NextStreamAsync();

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
			await Assert.That(HttpUtility.ParseQueryString(loop.PlayInfoQuery ?? string.Empty)["qn"]).IsEqualTo("400");
			await Assert.That(next.Host).IsEqualTo("b.test");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task RecordingLogShowsTheStreamPathWithoutTheQueryAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted.WaitAsync(cancellationToken);
		string logged = await Assert.That(loop.RecorderLogs.Where(static message => message.StartsWith("Recording ", StringComparison.Ordinal))).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(logged).Contains($"{request.Host}/live/avc.flv ");
			// B 站真实地址的查询参数里有用户的 uid
			await Assert.That(logged).DoesNotContain("sign=");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FFmpegFailureDoesNotShowTheSignedStreamAddressAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg);
		loop.FailFFmpeg("[http @ 0] Opening 'https://a.test/live/avc.flv?sign=1' for reading");
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.LastError).DoesNotContain("sign=");
			await Assert.That(loop.Logs.Where(static log => $"{log.Message} {log.Exception}".Contains("sign=", StringComparison.Ordinal))).IsEmpty();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task FFmpegFailureIsShownInTheAppLanguageWithItsLastOutputAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg);
		loop.FailFFmpeg("Connection refused");
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);
		string error = await Assert.That(loop.Snapshot.LastError).IsNotNull();

		using (Assert.Multiple())
		{
			await Assert.That(error).Contains(AppLanguage.Current.FFmpegFailed.ToString(1, "Connection refused"));
			await Assert.That(error).DoesNotContain("FFmpeg exited with code");
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ConnectionTimeoutIsShownInTheAppLanguageAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		// 三条线路都不应答
		for (int i = 0; i < 3; ++i)
		{
			_ = await loop.NextStreamAsync();
			loop.Time.Advance(loop.Settings.StreamConnectTimeout);
		}

		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);
		string error = await Assert.That(loop.Snapshot.LastError).IsNotNull();

		using (Assert.Multiple())
		{
			await Assert.That(error).IsEqualTo(AppLanguage.Current.RecordingStreamsUnavailable.ToString(AppLanguage.Current.RecordingEndpointFailed.ToString("Flv/Av1", "c.test", AppLanguage.Current.RecordingConnectionTimedOut)));
			await Assert.That(loop.Logs.Any(static log => log.Exception is TimeoutException)).IsTrue();
		}
	}
}
