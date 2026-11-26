using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomMasterPlaylistTest
{
	private const string AvcPlaylist = "/live-bvc/1/live_a_b_c_2500/index.m3u8";
	private const string HevcPlaylist = "/live-bvc/1/live_a_b_c_minihevc/index.m3u8";
	private const string AvcOriginalPlaylist = "/live-bvc/1/live_a_b_c_bluray/index.m3u8";
	private const string HevcOriginalPlaylist = "/live-bvc/1/live_a_b_c_prohevc/index.m3u8";
	private const string AvcBluRayPlaylist = "/live-bvc/1/live_a_b_c_4000/index.m3u8";
	private const string OriginalTrueColorPlaylist = "/live-bvc/1/live_a_b_c/index.m3u8";

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
		#EXT-X-STREAM-INF:BANDWIDTH=29000000,CODECS="hvc1.1.2.H150,mp4a.40.2",BILI-DISPLAY="原画真彩",BILI-QN=25000,PATHWAY-ID="https://a.test"
		https://a.test{OriginalTrueColorPlaylist}?sign=master

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
			AvcPlaylist or HevcPlaylist or AvcOriginalPlaylist or HevcOriginalPlaylist or AvcBluRayPlaylist or OriginalTrueColorPlaylist => Text(LivePlaylist),
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

	private static RecordingLoopHarness Loop(CancellationToken cancellationToken, string? streams = null, Func<string, HttpResponseMessage?>? respond = null, string? master = MasterPlaylist)
	{
		RecordingLoopHarness loop = new(cancellationToken, streams: streams ?? Fmp4Streams(), respondStream: Fmp4Cdn(respond));

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

	private static (StreamCodec Codec, StreamQuality Quality, QualityRestriction? Restriction)? Recorded(RecordingLoopHarness loop)
	{
		return loop.Snapshot.Recording is { } recording ? (recording.Codec, recording.Quality, recording.Restriction) : null;
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
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.Original, null));
			await Assert.That(loop.Snapshot.Recording?.Format).IsEqualTo(StreamFormat.Fmp4);
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.StreamRequests.First().Query).IsEqualTo("?sign=master");
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=0&").And.Contains("&qn=10000&");
			await Assert.That(loop.RecorderLogs).Contains(log => log.StartsWith($"Recording Fmp4/Avc at quality 10000 offered by the master playlist from a.test{AvcOriginalPlaylist} to ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.Contains(" is available: ", StringComparison.Ordinal));
		}
	}

	[Test]
	[Arguments(null, "The master playlist requested at quality 10000 is unavailable: ")]
	[Arguments("""{"code":-400,"message":"请求错误","ttl":1}""", "The master playlist requested at quality 10000 is unavailable: ")]
	[Arguments("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"avc1.4d401f\",BILI-QN=250\nhttps://a.test/live-bvc/1/live_a_b_c_2500/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=1,CODECS=\"hvc1.1.2.H150\",BILI-QN=25000\nhttps://a.test/live-bvc/1/live_a_b_c/index.m3u8\n", "The master playlist offers no stream from quality 250 to 10000 besides those from getRoomPlayInfo")]
	public async Task UnavailableMasterPlaylistFallsBackToTheListedStreamsAsync(string? master, string reason, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, master: master);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(StreamQuality.Original));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("The master playlist ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).Contains(log => log.StartsWith(reason, StringComparison.Ordinal));
			await Assert.That(loop.Logs).DoesNotContain(static log => log.Exception is not null && log.Message.Contains("master playlist", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.StartsWith("Failed to open", StringComparison.Ordinal));
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
		}
	}

	[Test]
	public async Task RejectedVariantFallsBackToTheListedStreamRightAwayAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, respond: static path => path is AvcOriginalPlaylist or HevcOriginalPlaylist or AvcBluRayPlaylist ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.SuperHigh, new QualityRestriction.NotLoggedIn(StreamQuality.Original)));
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcBluRayPlaylist, AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.RecorderLogs).Contains(static log => log.StartsWith($"Recording Fmp4/Avc at quality 250 offered by getRoomPlayInfo from a.test{AvcPlaylist} to ", StringComparison.Ordinal));
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log == "Recording at quality 250 while 10000 is available: not logged in");
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("Fmp4/Avc 10000 from the master playlist is not available from a.test: ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("Fmp4/Hevc 10000 from the master playlist is not available from a.test: ", StringComparison.Ordinal));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("Fmp4/Avc 400 from the master playlist is not available from a.test: ", StringComparison.Ordinal));
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
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcBluRayPlaylist, AvcPlaylist, HevcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.LastError).Contains("Fmp4/Hevc a.test");
			await Assert.That(loop.RecorderLogs.Where(static log => log.StartsWith("Failed to open", StringComparison.Ordinal))).Count().IsEqualTo(2);
		}
	}

	[Test]
	public async Task RejectedVariantIsNotRetriedAfterAQuickFailureAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, respond: static path => path switch
		{
			AvcOriginalPlaylist or HevcOriginalPlaylist or AvcBluRayPlaylist => new HttpResponseMessage(HttpStatusCode.Forbidden),
			AvcPlaylist => Text(LivePlaylist + "#EXT-X-ENDLIST\n"),
			_ => null
		});
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot is { RecordStatus: RecordStatus.Recording, Recording.Codec: StreamCodec.Hevc });

		using (Assert.Multiple())
		{
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcOriginalPlaylist, HevcOriginalPlaylist, AvcBluRayPlaylist, AvcPlaylist, HevcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.StreamRequests).HasSingleItem(static uri => uri.AbsolutePath == AvcOriginalPlaylist);
			await Assert.That(loop.PlayInfoRequests).IsEqualTo(1);
			await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();
		}
	}

	// 23197314 匿名用 qn=25000 请求时 accept_qn 是 [10000, 400, 250]，主播放列表里却有 25000
	[Test]
	public async Task QualityMissingFromTheAcceptListIsTakenFromTheMasterPlaylistAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Streams(10000, 10000, 400, 250));
		loop.QualityDescriptions = """[{"qn":10000,"desc":"原画"},{"qn":250,"desc":"超清"}]""";
		loop.Settings = loop.Settings with { Quality = StreamQuality.OriginalTrueColor };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoQuery).Contains("&qn=25000&");
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=0&").And.Contains("&qn=25000&");
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, StreamQuality.OriginalTrueColor, null));
			await Assert.That(Playlists(loop)).IsEquivalentTo([OriginalTrueColorPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.Snapshot.Recording?.QualityNames).IsEquivalentTo(new Dictionary<long, string> { [10000] = "原画", [250] = "超清", [25000] = "原画真彩" });
		}
	}

	// 接口和主播放列表给同一档起的名称不同时，变体的错误提示要和直播间列表的画质叫法一致
	[Test]
	public async Task VariantFailureNamesTheQualityLikeTheRoomListAsync(CancellationToken cancellationToken)
	{
		CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Streams(10000, 10000, 400, 250), static path => path is OriginalTrueColorPlaylist ? Text(LivePlaylist + "#EXT-X-ENDLIST\n") : null, MasterPlaylist.Replace("BILI-DISPLAY=\"原画真彩\"", "BILI-DISPLAY=\"2K 原画\"", StringComparison.Ordinal));
		loop.QualityDescriptions = """[{"qn":25000,"desc":"原画真彩"},{"qn":10000,"desc":"原画"}]""";
		loop.Settings = loop.Settings with { Quality = StreamQuality.OriginalTrueColor };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot is { RecordStatus: RecordStatus.Recording, LastError: not null });

		using (Assert.Multiple())
		{
			await Assert.That(Playlists(loop).First()).IsEqualTo(OriginalTrueColorPlaylist);
			await Assert.That(loop.Snapshot.Recording?.QualityNames[25000]).IsEqualTo("原画真彩");
			await Assert.That(loop.Snapshot.LastError).Contains(AppLanguage.Current.RecordingVariantStream.ToString(StreamFormat.Fmp4, StreamCodec.Hevc, "原画真彩"));
		}
	}

	[Test]
	[Arguments(10000L, new[] { OriginalTrueColorPlaylist, AvcPlaylist }, DisplayName = "接口给的原画就是下一档")]
	[Arguments(250L, new[] { OriginalTrueColorPlaylist, AvcOriginalPlaylist }, DisplayName = "主播放列表的原画是下一档")]
	public async Task UnavailableVariantFallsBackToTheNextBestAsync(long qn, string[] playlists, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Streams(qn), respond: static path => path is OriginalTrueColorPlaylist ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null);
		loop.Settings = loop.Settings with { Quality = StreamQuality.OriginalTrueColor };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(masterQuery).StartsWith($"?cid={RecordingLoopHarness.RoomId}&mid=0&").And.Contains("&qn=25000&");
			await Assert.That(Playlists(loop)).IsEquivalentTo(playlists, CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.Original, null));
		}
	}

	// 21622811 匿名时 AV1 是原画、AVC 是超清，这里用 HEVC 代替 AV1
	[Test]
	[Arguments(new[] { StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1 })]
	[Arguments(new[] { StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1 })]
	public async Task HigherQualityFromTheApiIsRecordedWhateverTheCodecPriorityAsync(StreamCodec[] codecPriority, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken, Fmp4Codecs(Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("hevc", HevcOriginalPlaylist, 10000, 10000, 250)));
		loop.Config.CodecPriority = codecPriority;
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();
			await Assert.That(Playlists(loop)).IsEquivalentTo([HevcOriginalPlaylist], CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, StreamQuality.Original, null));
		}
	}

	[Test]
	public async Task DefaultQualityRecordsTheApiStreamWithoutTheMasterPlaylistAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = Loop(cancellationToken);
		loop.Settings = loop.Settings with { Quality = StreamQuality.Default };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.PlayInfoQuery).Contains("&qn=0&");
			await Assert.That(loop.MasterPlaylistQueries).IsEmpty();
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.SuperHigh, null));
		}
	}

	// 主播放列表没有所选画质时，取不超过它、又比接口的流高的一档
	[Test]
	public async Task BestVariantBelowTheSelectionIsRecordedAsync(CancellationToken cancellationToken)
	{
		const string master = $"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=3800000,CODECS=\"avc1.4d4028\",BILI-QN=400\nhttps://a.test{AvcBluRayPlaylist}?sign=master\n";
		await using RecordingLoopHarness loop = Loop(cancellationToken, master: master);
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.BluRay, new QualityRestriction.NotLoggedIn(StreamQuality.Original)));
			await Assert.That(Playlists(loop)).IsEquivalentTo([AvcBluRayPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.RecorderLogs).Contains(static log => log.StartsWith($"Recording Fmp4/Avc at quality 400 offered by the master playlist from a.test{AvcBluRayPlaylist} to ", StringComparison.Ordinal));
		}
	}

	// 3990387 匿名时 AV1 是原画；编码优先级里 AVC 在前，也先试 AV1
	[Test]
	public async Task HigherQualityOfACodecThatFailedToOpenIsReportedAsUnavailableAsync(CancellationToken cancellationToken)
	{
		string streams = Fmp4Codecs(Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("av1", Av1Playlist, 10000, 10000, 400));
		await using RecordingLoopHarness loop = Loop(cancellationToken, streams, master: null);
		loop.Config.CodecPriority = [StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1];
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.SuperHigh, new QualityRestriction.OtherCodecUnavailable(StreamQuality.Original, StreamCodec.Av1, StreamQuality.Original)));
			await Assert.That(Playlists(loop)).IsEquivalentTo([Av1Playlist, AvcPlaylist], CollectionOrdering.Matching);
			await Assert.That(loop.RecorderLogs).Contains("Recording at quality 250 while 10000 is available: the Av1 stream offers quality 10000 but could not be opened");
		}
	}

	// 21622811 匿名时只有 AV1 是原画，AVC 和 HEVC 是超清：接口给的 AV1 打不开时先试主播放列表里其他编码的原画，再降画质
	[Test]
	[Arguments(25000L, new[] { OriginalTrueColorPlaylist, Av1Playlist, AvcOriginalPlaylist })]
	[Arguments(10000L, new[] { Av1Playlist, AvcOriginalPlaylist }, DisplayName = "接口已有所选画质")]
	public async Task OtherCodecsOfTheListedQualityAreTriedBeforeALowerOneAsync(long quality, string[] playlists, CancellationToken cancellationToken)
	{
		string streams = Fmp4Codecs(Codec("av1", Av1Playlist, 10000, 10000, 250), Codec("avc", AvcPlaylist, 250, 10000, 250), Codec("hevc", HevcPlaylist, 250, 10000, 250));
		await using RecordingLoopHarness loop = Loop(cancellationToken, streams, static path => path switch
		{
			OriginalTrueColorPlaylist => new HttpResponseMessage(HttpStatusCode.Forbidden),
			Av1Playlist => new HttpResponseMessage(HttpStatusCode.NotFound),
			_ => null
		});
		loop.Settings = loop.Settings with { Quality = new StreamQuality(quality) };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);
		string masterQuery = await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(masterQuery).Contains($"&qn={quality}&");
			await Assert.That(Playlists(loop)).IsEquivalentTo(playlists, CollectionOrdering.Matching);
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Avc, StreamQuality.Original, null));
		}
	}

	// 匿名用杜比的 qn 请求时不列原画真彩
	[Test]
	[Arguments(true, StreamCodec.Hevc, 25000L)]
	[Arguments(false, StreamCodec.Avc, 10000L, DisplayName = "改用原画真彩的 qn 请求失败时沿用第一次列出的变体")]
	public async Task DolbySelectionAsksForTheOriginalTrueColorAgainAsync(bool isRetryAvailable, StreamCodec codec, long qn, CancellationToken cancellationToken)
	{
		const string dolbyMaster = $"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=5000000,CODECS=\"avc1.640032\",BILI-QN=10000\nhttps://a.test{AvcOriginalPlaylist}?sign=master\n";
		await using RecordingLoopHarness loop = Loop(cancellationToken);
		loop.MasterPlaylist = request => HttpUtility.ParseQueryString(request.RequestUri?.Query ?? "")["qn"] switch
		{
			"30000" => Text(dolbyMaster),
			_ when isRetryAvailable => Text(MasterPlaylist),
			_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
		};
		loop.Settings = loop.Settings with { Quality = StreamQuality.Dolby };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		using (Assert.Multiple())
		{
			await Assert.That(string.Join(' ', loop.MasterPlaylistQueries.Select(static query => HttpUtility.ParseQueryString(query)["qn"]))).IsEqualTo("30000 25000");
			await Assert.That(Recorded(loop)).IsEqualTo((codec, new StreamQuality(qn), null));

			if (isRetryAvailable)
			{
				await Assert.That(loop.RecorderLogs).DoesNotContain(static log => log.StartsWith("The master playlist ", StringComparison.Ordinal));
			}
			else
			{
				await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log.StartsWith("The master playlist requested at quality 25000 is unavailable: ", StringComparison.Ordinal));
			}
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
			await Assert.That(Recorded(loop)).IsEqualTo((StreamCodec.Hevc, StreamQuality.Original, null));
			await Assert.That(loop.MasterPlaylistQueries).HasSingleItem();
			await Assert.That(loop.StreamRequests).HasSingleItem(static uri => uri.AbsolutePath == AvcPlaylist);
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
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.LoginMayHaveExpired(StreamQuality.Original));
			await Assert.That(loop.RecorderLogs).Contains("Recording at quality 250 while 10000 is available: the login may have expired");
		}
	}

	// 录制器把自己的日志交给 HLS 会话，换节点取到分片时日志里才看得到
	[Test]
	public async Task SegmentFetchedFromAnotherHostShowsInTheRecorderLogAsync(CancellationToken cancellationToken)
	{
		string streams = Fmp4Codecs($$"""{"codec_name":"avc","current_qn":10000,"accept_qn":[10000],"base_url":"{{AvcOriginalPlaylist}}","url_info":[{"host":"https://a.test","extra":"?sign=$SIGN"},{"host":"https://b.test","extra":"?sign=$SIGN"}]}""");
		int playlistRequests = 0;
		Func<HttpRequestMessage, HttpResponseMessage> cdn = Fmp4Cdn();
		await using RecordingLoopHarness loop = new(cancellationToken, streams: streams, respondStream: request => request.RequestUri switch
		{
			{ AbsolutePath: AvcOriginalPlaylist } => Text(Interlocked.Increment(ref playlistRequests) is 1 ? LivePlaylist : LivePlaylist + "#EXTINF:1,\n2.m4s\n"),
			{ Host: "a.test", AbsolutePath: var path } when path.EndsWith("/2.m4s", StringComparison.Ordinal) => new HttpResponseMessage(HttpStatusCode.NotFound),
			{ AbsolutePath: var path } when path.EndsWith("/2.m4s", StringComparison.Ordinal) => Bytes(Fmp4Samples.Fragment(true)),
			_ => cdn(request)
		});
		loop.Settings = loop.Settings with { StreamIdleTimeout = TimeSpan.FromHours(1) };
		loop.GoLive();

		await loop.WaitUntilAsync(static snapshot => snapshot.RecordStatus is RecordStatus.Recording);

		for (int step = 0; step < 30 && !loop.RecorderLogs.Any(static log => log.StartsWith("HLS segment ", StringComparison.Ordinal)); ++step)
		{
			loop.Time.Advance(TimeSpan.FromSeconds(1));
			await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
		}

		await Assert.That(loop.RecorderLogs).Contains(static log => log.StartsWith("HLS segment 2.m4s failed on a.test, fetched it from b.test ", StringComparison.Ordinal));
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
			await Assert.That(loop.Snapshot.Recording?.Restriction).IsEqualTo(new QualityRestriction.NotLoggedIn(StreamQuality.Original));
			await Assert.That(loop.RecorderLogs).HasSingleItem(static log => log == "Recording at quality 250 while 10000 is available: not logged in");
		}
	}
}
