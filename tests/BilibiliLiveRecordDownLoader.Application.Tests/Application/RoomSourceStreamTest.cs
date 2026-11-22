using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Globalization;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 接口给的 fMP4 都在 a.test，AVC 是 live_a_b_c_2500，HEVC 是 live_a_b_c_minihevc，去掉后缀都是 live_a_b_c
public sealed class RoomSourceStreamTest
{
	private const string SourcePlaylist = "/live-bvc/1/live_a_b_c/index.m3u8";
	private const string AvcPlaylist = "/live-bvc/1/live_a_b_c_2500/index.m3u8";

	private const string HevcPlaylist = "/live-bvc/1/live_a_b_c_minihevc/index.m3u8";

	// CDN 上没有这个播放列表，连不上
	private const string Av1Playlist = "/live-bvc/1/live_a_b_c_proav1/index.m3u8";

	// 没有 EXT-X-ENDLIST，录制会一直等新分片
	private const string LivePlaylist = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\n1.m4s\n";

	private static string Fmp4Streams(long qn = 250, long[]? accept = null)
	{
		long[] acceptQn = accept ?? [10000, 250];

		return Fmp4Streams(Codec("avc", AvcPlaylist, qn, acceptQn), Codec("hevc", HevcPlaylist, qn, acceptQn));
	}

	private static string Fmp4Streams(params string[] codecs)
	{
		return $$"""[{"protocol_name":"http_hls","format":[{"format_name":"fmp4","codec":[{{string.Join(',', codecs)}}]}]}]""";
	}

	private static string Codec(string name, string playlist, long qn, params long[] accept)
	{
		string acceptQn = string.Join(',', accept.Select(static value => value.ToString(CultureInfo.InvariantCulture)));

		return $$"""{"codec_name":"{{name}}","current_qn":{{qn}},"accept_qn":[{{acceptQn}}],"base_url":"{{playlist}}","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}""";
	}

	// avc 给出时代替 AVC 播放列表的应答
	private static Func<HttpRequestMessage, HttpResponseMessage> Fmp4Cdn(Func<string, HttpResponseMessage?>? source = null, string sourceInit = "hvc1", Func<HttpResponseMessage>? avc = null)
	{
		return request => request.RequestUri?.AbsolutePath switch
		{
			SourcePlaylist when source?.Invoke(SourcePlaylist) is { } response => response,
			AvcPlaylist when avc is not null => avc(),
			SourcePlaylist or AvcPlaylist or HevcPlaylist => Text(LivePlaylist),
			"/live-bvc/1/live_a_b_c/h1.m4s" => Bytes(FillerSamples.Init(FillerCodec.Hevc, 4, sourceInit)),
			"/live-bvc/1/live_a_b_c_2500/h1.m4s" => Bytes(FillerSamples.Init(FillerCodec.Avc, 4)),
			"/live-bvc/1/live_a_b_c_minihevc/h1.m4s" => Bytes(FillerSamples.Init(FillerCodec.Hevc, 4)),
			{ } path when path.EndsWith("/1.m4s", StringComparison.Ordinal) => Bytes(Fmp4Samples.Fragment(true)),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
	}

	private static HttpResponseMessage Text(string text)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/vnd.apple.mpegurl") };
	}

	private static HttpResponseMessage Bytes(byte[] data)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
	}

	// 依次打开过的播放列表
	private static string Paths(RecordingLoopHarness loop)
	{
		return string.Join(" ", loop.StreamRequests.Select(static uri => uri.AbsolutePath).Where(static path => path.EndsWith(".m3u8", StringComparison.Ordinal)).Distinct());
	}

	[Test]
	[Timeout(30_000)]
	public async Task SourceStreamIsTriedFirstAndRecordedWithTheCodecOfItsInitSegmentAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: Fmp4Cdn());
		loop.Config.TryOriginalWhenRestricted = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			// 来源是 AVC 线路，拿到的却是 HEVC 的源流
			await Assert.That(loop.Snapshot.Recording is { Format: StreamFormat.Fmp4, Codec: StreamCodec.Hevc, Quality: 10000, Restriction: null }).IsTrue();
			await Assert.That(Paths(loop)).IsEqualTo(SourcePlaylist);
			await Assert.That(loop.StreamRequests.First().Query).IsEqualTo("?sign=1");
			await Assert.That(loop.RecorderLogs.Any(static log => log.StartsWith($"Recording Fmp4/Hevc at quality 10000 from a.test{SourcePlaylist} to ", StringComparison.Ordinal))).IsTrue();
			await Assert.That(loop.RecorderLogs.Any(static log => log.Contains(" is available: ", StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task SourceBehindAMasterPlaylistIsFollowedAsync(CancellationToken cancellationToken)
	{
		Func<HttpRequestMessage, HttpResponseMessage> cdn = Fmp4Cdn(static _ => Text("#EXTM3U\n#EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2560000\nhttps://a.test/live-bvc/1/live_a_b_c/variant.m3u8?sign=edge\n"));
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: request => request.RequestUri?.AbsolutePath is "/live-bvc/1/live_a_b_c/variant.m3u8" ? Text("#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\n1.m4s\n") : cdn(request));
		loop.Config.TryOriginalWhenRestricted = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Codec).IsEqualTo(StreamCodec.Hevc);
			await Assert.That(loop.Snapshot.Recording?.Quality).IsEqualTo(10000);
			await Assert.That(Paths(loop)).IsEqualTo($"{SourcePlaylist} /live-bvc/1/live_a_b_c/variant.m3u8");
		}
	}

	// 源流被拒是立即返回的，同一节点上接口给的流照常可用，不必等重连间隔，也不必重新请求候选流
	[Test]
	[Timeout(30_000)]
	[Arguments(HttpStatusCode.Forbidden)]
	[Arguments(HttpStatusCode.NotFound)]
	public async Task RejectedSourceFallsBackToTheListedStreamRightAwayAsync(HttpStatusCode status, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: Fmp4Cdn(_ => new HttpResponseMessage(status)));
		loop.Config.TryOriginalWhenRestricted = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Codec).IsEqualTo(StreamCodec.Avc);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
			await Assert.That(Paths(loop)).IsEqualTo($"{SourcePlaylist} {AvcPlaylist}");
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs.Count(static log => log == "Recording at quality 250 while 10000 is available: not logged in")).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs.Count(static log => log.StartsWith("The source stream is not available from a.test", StringComparison.Ordinal))).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs.Any(static log => log.StartsWith("Failed to open", StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	// 开关关着
	[Arguments(false, false, 250L, "NotLoggedIn")]
	// 首选的流已经是原画
	[Arguments(true, false, 10000L, null)]
	// 开了真原画拿到普通原画也不算受限
	[Arguments(true, true, 10000L, null)]
	public async Task SourceIsOnlyTriedWhenTurnedOnAndTheQualityIsRestrictedAsync(bool tryOriginal, bool requestTrueOriginal, long qn, string? restriction, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(qn), respondStream: Fmp4Cdn());
		loop.Config.TryOriginalWhenRestricted = tryOriginal;
		loop.Config.RequestTrueOriginal = requestTrueOriginal;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Paths(loop)).IsEqualTo(AvcPlaylist);
			await Assert.That(loop.Snapshot.Recording?.Restriction?.GetType().Name).IsEqualTo(restriction);
		}
	}

	// 源流是原画：选了蓝光时录它违背用户的选择，选了 4K 而接口列出 4K 时它会抢在 4K 的流前面
	[Test]
	[Timeout(30_000)]
	[Arguments(StreamQuality.BluRay, new long[] { 10000, 400, 250 }, 400L)]
	[Arguments(StreamQuality.UltraHd4K, new long[] { 20000, 10000, 250 }, 20000L)]
	// 所选的蓝光不在 accept_qn 里，超清就是不高于它的最高一档
	[Arguments(StreamQuality.BluRay, new long[] { 10000, 250 }, null)]
	public async Task SourceIsOnlyDerivedWhenTheTargetIsTheOriginalAsync(StreamQuality quality, long[] accept, long? highest, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(250, accept), respondStream: Fmp4Cdn());
		loop.Config.TryOriginalWhenRestricted = true;
		loop.Settings.Quality = quality;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Paths(loop)).IsEqualTo(AvcPlaylist);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(highest is { } value ? new QualityRestriction.NotLoggedIn(value) : null);
		}
	}

	// 判断按所选的原画，不按实际请求的 25000
	[Test]
	[Timeout(30_000)]
	public async Task OriginalAfterAskingForTheTrueOriginalIsNotRestrictedAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(10000, [25000, 10000, 250]), respondStream: Fmp4Cdn());
		loop.Config.RequestTrueOriginal = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoQuery).Contains("&qn=25000&");
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsNull();
			await Assert.That(loop.RecorderLogs.Any(static log => log.Contains(" is available: ", StringComparison.Ordinal))).IsFalse();
		}
	}

	// 32378653 匿名时 AVC 列出原画和超清，HEVC 只列超清，两者都给超清；只录 HEVC 也要按整次返回里的原画判断
	[Test]
	[Timeout(30_000)]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictionComesFromTheAcceptListsOfEveryCodecAsync(bool tryOriginal, CancellationToken cancellationToken)
	{
		string streams = Fmp4Streams(Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("hevc", HevcPlaylist, 250, 250));
		await using RecordingLoopHarness loop = new(cancellationToken, streams: streams, respondStream: Fmp4Cdn(static _ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
		loop.Config.TryOriginalWhenRestricted = tryOriginal;
		loop.Config.CodecPriority = [StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1];
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Codec).IsEqualTo(StreamCodec.Hevc);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
			await Assert.That(Paths(loop)).IsEqualTo(tryOriginal ? $"{SourcePlaylist} {HevcPlaylist}" : HevcPlaylist);
		}
	}

	// 3990387 匿名时 AV1 是原画；AV1 排在前面时已经先试过，连不上时调整编码优先级没用
	[Test]
	[Timeout(30_000)]
	[Arguments(new[] { StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1 }, false)]
	[Arguments(new[] { StreamCodec.Av1, StreamCodec.Avc, StreamCodec.Hevc }, true)]
	public async Task HigherQualityOfAnotherCodecIsReportedByWhetherItWasTriedAsync(StreamCodec[] codecPriority, bool triedFirst, CancellationToken cancellationToken)
	{
		string streams = Fmp4Streams(Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("av1", Av1Playlist, 10000, 10000, 400));
		await using RecordingLoopHarness loop = new(cancellationToken, streams: streams, respondStream: Fmp4Cdn());
		loop.Config.CodecPriority = [.. codecPriority];
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		QualityRestriction expected = triedFirst ? new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000) : new QualityRestriction.OtherCodec(10000, StreamCodec.Av1, 10000);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Codec).IsEqualTo(StreamCodec.Avc);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(expected);
			await Assert.That(Paths(loop)).IsEqualTo(triedFirst ? $"{Av1Playlist} {AvcPlaylist}" : AvcPlaylist);
			await Assert.That(loop.RecorderLogs).Contains(triedFirst ? "Recording at quality 250 while 10000 is available: the Av1 stream offers quality 10000 but could not be opened" : "Recording at quality 250 while 10000 is available: the Av1 stream offers quality 10000");
		}
	}

	// 接口给的流很快断开时沿用这批候选流换下一路，被拒的源流不再重试
	[Test]
	[Timeout(30_000)]
	public async Task RejectedSourceIsNotRetriedAfterAQuickFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: Fmp4Cdn(static _ => new HttpResponseMessage(HttpStatusCode.Forbidden), avc: static () => Text(LivePlaylist + "#EXT-X-ENDLIST\n")));
		loop.Config.TryOriginalWhenRestricted = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot is { RecordStatus: RecordStatus.Recording, Recording.Codec: StreamCodec.Hevc });

		using (Assert.Multiple())
		{
			await Assert.That(Paths(loop)).IsEqualTo($"{SourcePlaylist} {AvcPlaylist} {HevcPlaylist}");
			await Assert.That(loop.StreamRequests.Count(static uri => uri.AbsolutePath == SourcePlaylist)).IsEqualTo(1);
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	// 未登录时拿到的候选流不能配上登录后的身份判断；失败的线路仍然记着
	[Test]
	[Timeout(30_000)]
	public async Task CandidatesAreRequestedAgainAfterLoggingInAsync(CancellationToken cancellationToken)
	{
		Action? onAvc = null;
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: Fmp4Cdn(avc: () =>
		{
			onAvc?.Invoke();
			return Text(LivePlaylist + "#EXT-X-ENDLIST\n");
		}));
		onAvc = () =>
		{
			loop.Config.Cookie = "SESSDATA=x";
			loop.Streams = Fmp4Streams(10000);
		};
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot is { RecordStatus: RecordStatus.Recording, Recording.Codec: StreamCodec.Hevc });

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
			await Assert.That(loop.Snapshot.Recording is { Quality: 10000, Restriction: null }).IsTrue();
			await Assert.That(loop.StreamRequests.Count(static uri => uri.AbsolutePath == AvcPlaylist)).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs.Any(static log => log.Contains("the login may have expired", StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ConfiguredCookieTurnsTheRestrictionIntoAPossiblyExpiredLoginAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, streams: Fmp4Streams(), respondStream: Fmp4Cdn());
		loop.Config.Cookie = "SESSDATA=x";
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(10000));
			await Assert.That(loop.RecorderLogs).Contains("Recording at quality 250 while 10000 is available: the login may have expired");
		}
	}

	// FFmpeg 重新拉流前就要认出编码，认错了会按错误的 NAL 类型过滤
	[Test]
	[Timeout(30_000)]
	[Arguments("hvc1", "filter_units=remove_types=38")]
	[Arguments("avc3", "filter_units=remove_types=12")]
	[Arguments("vp09", null)]
	public async Task FFmpegRemovesFillerByTheCodecOfTheSourceStreamAsync(string sampleEntry, string? filter, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken, recorderType: RecorderType.FFmpeg, streams: Fmp4Streams(), respondStream: Fmp4Cdn(sourceInit: sampleEntry));
		loop.Config.TryOriginalWhenRestricted = true;
		loop.Config.RemoveFillerData = true;
		loop.GoLive();

		await loop.WaitUntilAsync(_ => !loop.FFmpegRuns.IsEmpty);
		string[] arguments = [.. loop.FFmpegRuns.First()];
		int bsf = Array.IndexOf(arguments, "-bsf:v");

		using (Assert.Multiple())
		{
			await Assert.That(arguments[Array.IndexOf(arguments, "-i") + 1]).IsEqualTo($"https://a.test{SourcePlaylist}?sign=1");
			await Assert.That(bsf < 0 ? null : arguments[bsf + 1]).IsEqualTo(filter);
		}
	}

	// FLV 和 TS 去掉后缀全部被拒，不派生；重连后受限情况没变，不再重复警告
	[Test]
	[Timeout(30_000)]
	public async Task RestrictionIsLoggedOncePerLiveAndFlvIsNotDerivedAsync(CancellationToken cancellationToken)
	{
		const string streams = """
			[{"protocol_name":"http_stream","format":[{"format_name":"flv","codec":[
				{"codec_name":"avc","current_qn":250,"accept_qn":[10000,250],"base_url":"/live-bvc/1/live_a_b_c_2500.flv","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}
			]}]}]
			""";
		await using RecordingLoopHarness loop = new(cancellationToken, streams: streams);
		loop.Config.TryOriginalWhenRestricted = true;
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		StreamRequest reconnected = await loop.NextStreamAsync();
		await reconnected.AcceptAsync();
		await reconnected.CopyStarted;
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
			await Assert.That(loop.StreamRequests.All(static uri => uri.AbsolutePath == "/live-bvc/1/live_a_b_c_2500.flv")).IsTrue();
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
			await Assert.That(loop.RecorderLogs.Count(static log => log == "Recording at quality 250 while 10000 is available: not logged in")).IsEqualTo(1);
		}
	}
}
