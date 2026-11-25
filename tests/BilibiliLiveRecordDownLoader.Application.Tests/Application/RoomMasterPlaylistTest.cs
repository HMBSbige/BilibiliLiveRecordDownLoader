using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Globalization;
using System.Net;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomMasterPlaylistTest
{
	private const string AvcPlaylist = "/live-bvc/1/live_a_b_c_2500/index.m3u8";
	private const string HevcPlaylist = "/live-bvc/1/live_a_b_c_minihevc/index.m3u8";
	private const string AvcOriginalPlaylist = "/live-bvc/1/live_a_b_c_bluray/index.m3u8";
	private const string HevcOriginalPlaylist = "/live-bvc/1/live_a_b_c_prohevc/index.m3u8";
	private const string AvcBluRayPlaylist = "/live-bvc/1/live_a_b_c_4000/index.m3u8";
	private const string TrueOriginalPlaylist = "/live-bvc/1/live_a_b_c/index.m3u8";

	private const string Av1Playlist = "/live-bvc/1/live_a_b_c_proav1/index.m3u8";

	private const string LivePlaylist = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MAP:URI=\"h1.m4s\"\n#EXTINF:1,\n1.m4s\n";

	// 按实测的样子，有的变体把视频编码写了两遍
	private const string MasterPlaylist = $"""
		#EXTM3U
		#EXT-X-CONTENT-STEERING:SERVER-URI="reserved",PATHWAY-ID="https://a.test"
		#EXT-X-STREAM-INF:BANDWIDTH=3000000,CODECS="hvc1.1.6.L150,mp4a.40.2",BILI-QN=10000,PATHWAY-ID="https://a.test"
		https://a.test{HevcOriginalPlaylist}?sign=master
		#EXT-X-STREAM-INF:BANDWIDTH=5000000,CODECS="avc1.640032,avc1.640032",BILI-QN=10000,PATHWAY-ID="https://a.test"
		https://a.test{AvcOriginalPlaylist}?sign=master
		#EXT-X-STREAM-INF:BANDWIDTH=3800000,CODECS="avc1.4d4028,avc1.4d4028",BILI-QN=400,PATHWAY-ID="https://a.test"
		https://a.test{AvcBluRayPlaylist}?sign=master
		#EXT-X-STREAM-INF:BANDWIDTH=2100000,CODECS="avc1.4d401f,mp4a.40.2",BILI-QN=250,PATHWAY-ID="https://a.test"
		https://a.test{AvcPlaylist}?sign=master
		#EXT-X-STREAM-INF:BANDWIDTH=29000000,CODECS="hvc1.1.2.H150,mp4a.40.2",BILI-QN=25000,PATHWAY-ID="https://a.test"
		https://a.test{TrueOriginalPlaylist}?sign=master

		""";

	private static string Fmp4Streams(long qn = 250, params long[] accept)
	{
		long[] acceptQn = accept.Length is 0 ? [10000, 250] : accept;

		return Fmp4Codecs(Codec("avc", AvcPlaylist, qn, acceptQn), Codec("hevc", HevcPlaylist, qn, acceptQn));
	}

	private static string Fmp4Codecs(params string[] codecs)
	{
		return $$"""[{"protocol_name":"http_hls","format":[{"format_name":"fmp4","codec":[{{string.Join(',', codecs)}}]}]}]""";
	}

	private static string Codec(string name, string playlist, long qn, params long[] accept)
	{
		string acceptQn = string.Join(',', accept.Select(static value => value.ToString(CultureInfo.InvariantCulture)));

		return $$"""{"codec_name":"{{name}}","current_qn":{{qn}},"accept_qn":[{{acceptQn}}],"base_url":"{{playlist}}","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}""";
	}

	private static Func<HttpRequestMessage, HttpResponseMessage> Fmp4Cdn(Func<string, HttpResponseMessage?>? respond = null)
	{
		return request => request.RequestUri?.AbsolutePath switch
		{
			{ } path when respond?.Invoke(path) is { } response => response,
			AvcPlaylist or HevcPlaylist or AvcOriginalPlaylist or HevcOriginalPlaylist or AvcBluRayPlaylist or TrueOriginalPlaylist => Text(LivePlaylist),
			{ } path when path.EndsWith("/h1.m4s", StringComparison.Ordinal) => Bytes(FillerSamples.Init(path.Contains("hevc", StringComparison.Ordinal) || path.StartsWith("/live-bvc/1/live_a_b_c/", StringComparison.Ordinal) ? FillerCodec.Hevc : FillerCodec.Avc, 4)),
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

	private static RecordingLoopHarness Loop(CancellationToken cancellationToken, string? streams = null, Func<string, HttpResponseMessage?>? respond = null, string? master = MasterPlaylist, RecorderType recorderType = RecorderType.BuiltIn)
	{
		RecordingLoopHarness loop = new(cancellationToken, recorderType: recorderType, streams: streams ?? Fmp4Streams(), respondStream: Fmp4Cdn(respond));

		if (master is not null)
		{
			loop.MasterPlaylist = _ => Text(master);
		}

		return loop;
	}

	private static IEnumerable<string> Playlists(RecordingLoopHarness loop)
	{
		return loop.StreamRequests.Select(static uri => uri.AbsolutePath).Where(static path => path.EndsWith(".m3u8", StringComparison.Ordinal)).Distinct();
	}

	private static (StreamCodec Codec, long Quality, QualityRestriction? Restriction)? Recorded(RecordingLoopHarness loop)
	{
		return loop.Snapshot.Recording is { } recording ? (recording.Codec, recording.Quality, recording.Restriction) : null;
	}

	private static IEnumerable<string> LogsStartingWith(RecordingLoopHarness loop, string prefix)
	{
		return loop.RecorderLogs.Where(log => log.StartsWith(prefix, StringComparison.Ordinal));
	}

	[Test]
	public async Task RestrictedRoomRecordsTheTargetFromTheMasterPlaylistAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken);
		loop.Config.CodecPriority = [StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1];
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, 10000L, null));
			await Assert.That(loop.Snapshot.Recording?.Format).IsEqualTo(StreamFormat.Fmp4);
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.StreamRequests.First().Query).IsEqualTo("?sign=master");
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=0&");
			await Assert.That(loop.RecorderLogs).Contains(log => log.StartsWith($"Recording Fmp4/Avc at quality 10000 from a.test{AvcOriginalPlaylist} to ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.Contains(" is available: ", StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task MasterPlaylistIsNotRequestedWhenTurnedOffAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken);
		loop.Config.UseMasterPlaylistWhenRestricted = false;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).IsEmpty();
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.Recording?.Restriction?.GetType().Name).IsEqualTo(nameof(QualityRestriction.NotLoggedIn));
		}
	}

	[Test]
	[Arguments(null)]
	[Arguments("""{"code":-400,"message":"请求错误","ttl":1}""")]
	[Arguments("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"avc1.4d4028\",BILI-QN=400\nhttps://a.test/live-bvc/1/live_a_b_c_4000/index.m3u8\n")]
	public async Task UnavailableMasterPlaylistFallsBackToTheListedStreamsAsync(string? master, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, master: master);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).Count().IsEqualTo(1);
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("The master playlist offers no stream of quality 10000: ", StringComparison.Ordinal));
			await Assert.That(loop.Logs).DoesNotContain(static log => log.Exception is not null && log.Message.Contains("master playlist", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.StartsWith("Failed to open", StringComparison.Ordinal));
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task RejectedVariantFallsBackToTheListedStreamRightAwayAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, respond: static path => path is AvcOriginalPlaylist or HevcOriginalPlaylist ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, 250L, new QualityRestriction.NotLoggedIn(10000)));
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log == "Recording at quality 250 while 10000 is available: not logged in");
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("Fmp4/Avc 10000 from the master playlist is not available from a.test: ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("Fmp4/Hevc 10000 from the master playlist is not available from a.test: ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.StartsWith("Failed to open", StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task LastFailureComesFromTheListedStreamsAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, respond: static path => path.EndsWith(".m3u8", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.LastError is not null);

		using (Assert.Multiple())
		{
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcPlaylist, HevcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.LastError).Contains("Fmp4/Hevc a.test");
			await Assert.That(LogsStartingWith(loop, "Failed to open")).Count().IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedVariantIsNotRetriedAfterAQuickFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, respond: static path => path switch
		{
			AvcOriginalPlaylist or HevcOriginalPlaylist => new HttpResponseMessage(HttpStatusCode.Forbidden),
			AvcPlaylist => Text(LivePlaylist + "#EXT-X-ENDLIST\n"),
			_ => null
		});
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot is { RecordStatus: RecordStatus.Recording, Recording.Codec: StreamCodec.Hevc });

		using (Assert.Multiple())
		{
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcPlaylist, HevcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.StreamRequests.Where(static uri => uri.AbsolutePath == AvcOriginalPlaylist)).Count().IsEqualTo(1);
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
			await Assert.That(loop.MasterPlaylistQueries).Count().IsEqualTo(1);
		}
	}

	[Test]
	public async Task MissingTrueOriginalIsTakenFromTheMasterPlaylistAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Streams(10000, 10000, 250));
		loop.Config.Cookie = "SESSDATA=x";
		loop.Config.RequestTrueOriginal = true;
		loop.Config.UseMasterPlaylistWhenRestricted = false;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoQuery).Contains("&qn=25000&");
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=1&");
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, 25000L, null));
			await Assert.That(Playlists(loop)).IsEquivalentTo([TrueOriginalPlaylist], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments(10000L, new[] { TrueOriginalPlaylist, AvcPlaylist }, DisplayName = "接口给的是原画")]
	[Arguments(250L, new[] { TrueOriginalPlaylist, AvcOriginalPlaylist }, DisplayName = "接口给的画质受限")]
	public async Task UnavailableTrueOriginalVariantFallsBackToTheOriginalAsync(long qn, string[] playlists, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Streams(qn), respond: static path => path is TrueOriginalPlaylist ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
		loop.Config.RequestTrueOriginal = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=1&");
			await Assert.That(Playlists(loop)).IsEquivalentTo(playlists, CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, 10000L, null));
		}
	}

	// 接口只在 HEVC 上给真原画
	[Test]
	public async Task TrueOriginalFromTheApiIsTriedBeforeThePreferredCodecAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Codecs(Codec("avc", AvcPlaylist, 10000, 10000, 250), Codec("hevc", TrueOriginalPlaylist, 25000, 25000, 10000)));
		loop.Config.RequestTrueOriginal = true;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).IsEmpty();
			await Assert.That(Playlists(loop)).IsEquivalentTo([TrueOriginalPlaylist], CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, 25000L, null));
		}
	}

	// 3990387 匿名时 AV1 是原画
	[Test]
	public async Task HigherQualityOfACodecThatFailedToOpenIsReportedAsUnavailableAsync(CancellationToken cancellationToken)
	{
		string streams = Fmp4Codecs(Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("av1", Av1Playlist, 10000, 10000, 400));
		await using RecordingLoopHarness loop = Loop(cancellationToken, streams, master: null);
		loop.Config.CodecPriority = [StreamCodec.Av1, StreamCodec.Avc, StreamCodec.Hevc];
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, 250L, new QualityRestriction.OtherCodecUnavailable(10000, StreamCodec.Av1, 10000)));
			await Assert.That(Playlists(loop)).IsEquivalentTo([Av1Playlist, AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.RecorderLogs).Contains("Recording at quality 250 while 10000 is available: the Av1 stream offers quality 10000 but could not be opened");
		}
	}

	[Test]
	public async Task CandidatesAreRequestedAgainAfterLoggingInAsync(CancellationToken cancellationToken)
	{
		Action? onAvc = null;
		await using RecordingLoopHarness loop = Loop(cancellationToken, master: null, respond: path =>
		{
			if (path is not AvcPlaylist)
			{
				return null;
			}

			// ReSharper disable once AccessToModifiedClosure
			onAvc?.Invoke();
			return Text(LivePlaylist + "#EXT-X-ENDLIST\n");
		});
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
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, 10000L, null));
			await Assert.That(loop.StreamRequests.Where(static uri => uri.AbsolutePath == AvcPlaylist)).Count().IsEqualTo(1);
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.Contains("the login may have expired", StringComparison.Ordinal));
		}
	}

	[Test]
	public async Task ConfiguredCookieTurnsTheRestrictionIntoAPossiblyExpiredLoginAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, master: null);
		loop.Config.Cookie = "SESSDATA=x";
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(10000));
			await Assert.That(loop.RecorderLogs).Contains("Recording at quality 250 while 10000 is available: the login may have expired");
		}
	}

	[Test]
	[Arguments(new[] { StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1 }, AvcOriginalPlaylist, "filter_units=remove_types=12")]
	[Arguments(new[] { StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1 }, HevcOriginalPlaylist, "filter_units=remove_types=38")]
	public async Task FFmpegRemovesFillerByTheCodecOfTheVariantAsync(StreamCodec[] codecPriority, string playlist, string filter, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, recorderType: RecorderType.FFmpeg);
		loop.Config.CodecPriority = [.. codecPriority];
		loop.Config.RemoveFillerData = true;
		loop.GoLive();

		await loop.WaitUntilAsync(() => !loop.FFmpegRuns.IsEmpty, "FFmpeg is started");
		string[] arguments = [.. loop.FFmpegRuns.First()];

		using (Assert.Multiple())
		{
			await Assert.That(arguments[Array.IndexOf(arguments, "-i") + 1]).IsEqualTo($"https://a.test{playlist}?sign=master");
			await Assert.That(arguments[Array.IndexOf(arguments, "-bsf:v") + 1]).IsEqualTo(filter);
		}
	}

	[Test]
	public async Task RestrictionIsLoggedOncePerLiveAsync(CancellationToken cancellationToken)
	{
		const string streams = """
			[{"protocol_name":"http_stream","format":[{"format_name":"flv","codec":[
				{"codec_name":"avc","current_qn":250,"accept_qn":[10000,250],"base_url":"/live-bvc/1/live_a_b_c_2500.flv","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"}]}
			]}]}]
			""";
		await using RecordingLoopHarness loop = new(cancellationToken, streams: streams);
		loop.GoLive();

		await loop.RecordForAsync(await loop.NextStreamAsync(), RecordingAttempt.QuickFailureThreshold + TimeSpan.FromSeconds(1));
		StreamRequest reconnected = await loop.NextStreamAsync();
		await reconnected.AcceptAsync();
		await reconnected.CopyStarted;
		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(2);
			await Assert.That(loop.StreamRequests).All(static uri => uri.AbsolutePath == "/live-bvc/1/live_a_b_c_2500.flv");
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(10000));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log == "Recording at quality 250 while 10000 is available: not logged in");
		}
	}
}
