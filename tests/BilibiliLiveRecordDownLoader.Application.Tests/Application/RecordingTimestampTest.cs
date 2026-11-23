using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RecordingTimestampTest
{
	private const long PtsPeriod = 1L << 33;
	private const uint VideoTimescale = 90_000;
	private const uint AudioTimescale = 48_000;

	// fMP4 源 tfdt 的起点，秒
	private const ulong Fmp4Source = 14_204;

	private static readonly RecordingSplit OneMinute = new RecordingSplit.ByDuration(TimeSpan.FromMinutes(1));

	[Test]
	public async Task FlvFirstFileStartsAtItsFirstFrameWhereTheDanmakuIsPlacedAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new FlvSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. FlvSamples.Header, .. FlvSamples.Metadata(), .. FlvSamples.Video(VideoStyle.Avc, VideoPacket.SequenceHeader, 0, "vseq"), .. FlvSamples.AudioSequenceHeader()]);

			foreach (uint timestamp in (uint[])[8_418_000, 8_419_000, 8_420_000])
			{
				session.Advance(TimeSpan.FromSeconds(1));
				await session.WriteAsync([.. FlvSamples.Video(VideoStyle.Avc, timestamp is 8_418_000 ? VideoPacket.KeyFrame : VideoPacket.InterFrame, timestamp, "v"), .. FlvSamples.Audio(timestamp + 10, "a")]);
				times.Add(session.Now);
			}
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(FlvSamples.Describe(file)).IsEquivalentTo(["S0", "V0[vseq]", "A0", "V0[v]", "A10[a]", "V1000[v]", "A1010[a]", "V2000[v]", "A2010[a]"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@10", "0@1010", "0@2010"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task Fmp4TracksShareOneStartInEveryFileAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new Fmp4HlsSplitter(), OneMinute, async session =>
		{
			await session.InitAsync(Fmp4Init());

			foreach (byte[] fragment in (byte[][])[Fmp4Fragment(Fmp4Source * VideoTimescale, Fmp4Source * AudioTimescale - 480), Fmp4Fragment((Fmp4Source + 2) * VideoTimescale, (Fmp4Source + 2) * AudioTimescale - 480), Fmp4Fragment(900, 0)])
			{
				await session.SegmentAsync(fragment);
				times.Add(session.Now);
				session.Advance(TimeSpan.FromMinutes(1));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. Fmp4Init(), .. Fmp4Fragment(900, 0)], [.. Fmp4Init(), .. Fmp4Fragment(900, 0), .. Fmp4Fragment(181_800, 96_480)]], CollectionOrdering.Matching);
			await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V900", "A0"], ["V900", "A0", "V181800", "A96480"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2010", "1@2010", "1@4020"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task Fmp4TrackTimesAreRoundedUpFromTheSharedStartAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), static async session =>
		{
			await session.InitAsync(Fmp4Init(44_100));
			await session.SegmentAsync(Fmp4MovieFragment(VideoTraf(900, 1, 60), AudioTraf(1, 0, 96_000)));
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. Fmp4Init(44_100), .. Fmp4MovieFragment(VideoTraf(897, 1, 60), AudioTraf(0, 0, 96_000))]], CollectionOrdering.Matching);
			await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V897", "A0"]], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments((byte)1)]
	public async Task Fmp4TrackSlightlyBeforeTheStartIsClampedToZeroAsync(byte audioVersion, CancellationToken cancellationToken)
	{
		SplitRecording recording = new();

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(Fmp4Init());
			await session.SegmentAsync([.. Fmp4MovieFragment(VideoTraf(10 * VideoTimescale, 1, 1)), .. Fmp4MovieFragment(AudioTraf(10 * AudioTimescale - 960, audioVersion, 960))]);
		}, cancellationToken);

		await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V0", "A0"]], CollectionOrdering.Matching);
	}

	[Test]
	public async Task Fmp4FragmentThatGoesBackLessThanItsLengthContinuesAfterTheWrittenTimeAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(Fmp4Init());

			foreach (byte[] fragment in (byte[][])[Fmp4Fragment(Fmp4Source * VideoTimescale, Fmp4Source * AudioTimescale), Fmp4Fragment((Fmp4Source + 2) * VideoTimescale, (Fmp4Source + 2) * AudioTimescale), Fmp4Fragment(Fmp4Source * VideoTimescale + 225_000, Fmp4Source * AudioTimescale + 120_000)])
			{
				await session.SegmentAsync(fragment);
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(2));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V0", "A0", "V180000", "A96000", "V360000", "A192000"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2000", "0@4000", "0@6000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task Fmp4TimelineFollowsEveryMoofOfASegmentAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(Fmp4Init());
			await session.SegmentAsync([.. Fmp4Fragment(Fmp4Source * VideoTimescale, Fmp4Source * AudioTimescale), .. Fmp4Fragment((Fmp4Source + 2) * VideoTimescale, (Fmp4Source + 2) * AudioTimescale)]);
			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(4));
			await session.SegmentAsync([.. Fmp4Fragment((Fmp4Source + 4) * VideoTimescale, (Fmp4Source + 4) * AudioTimescale), .. Fmp4Fragment(0, 0)]);
			times.Add(session.Now);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V0", "A0", "V180000", "A96000", "V360000", "A192000", "V540000", "A288000"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@4000", "0@8000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task Fmp4Version0DecodeTimesKeepCountingWhereEachTrackWrapsAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new Fmp4HlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.InitAsync(Fmp4Init());

			// 47721 秒的视频时间还在 2³² 以内，47723 秒的已经回绕
			for (ulong seconds = 47_721; seconds <= 47_725; seconds += 2)
			{
				await session.SegmentAsync(Fmp4Fragment(seconds * VideoTimescale, seconds * AudioTimescale, 0));
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(2));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Select(DescribeDecodeTimes)).IsEquivalentTo((string[][])[["V0", "A0", "V180000", "A96000", "V360000", "A192000"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2000", "0@4000", "0@6000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TransportStreamTimestampsShareOneStartAcrossTheWrapAsync(CancellationToken cancellationToken)
	{
		const long start = PtsPeriod - 4_000;
		const long next = 45_000;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, start + 3003, start, start - 9000, true), .. TsSamples.TimedPes(TsSamples.AudioPid, start - 900), .. TsSamples.TimedPes(TsSamples.VideoPid, start + 9009, start + 3003, start + 1001)]);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync([.. TsSamples.TimedPes(TsSamples.VideoPid, next + 3003, next, next - 9000, true), .. TsSamples.TimedPes(TsSamples.AudioPid, next - 900), .. TsSamples.TimedPes(TsSamples.VideoPid, next + 93_003, next + 90_000, null, true)]);
			times.Add(session.Now);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, 3903, 900, 0, true), .. TsSamples.TimedPes(TsSamples.AudioPid, 0), .. TsSamples.TimedPes(TsSamples.VideoPid, 9909, 3903, 1901)], [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, 3903, 900, 0, true), .. TsSamples.TimedPes(TsSamples.AudioPid, 0), .. TsSamples.TimedPes(TsSamples.VideoPid, 93_903, 90_900, null, true)]], CollectionOrdering.Matching);
			await Assert.That(recording.Files.Select(DescribeTimestamps)).IsEquivalentTo((string[][])[["V:c0,p3903,d900", "A:p0", "V:c1901,p9909,d3903"], ["V:c0,p3903,d900", "A:p0", "V:p93903,d90900"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["1@1010"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TransportStreamWhosePtsWrapsBeforeItsDtsStaysOnTheTimelineAsync(CancellationToken cancellationToken)
	{
		const long start = PtsPeriod - 1_800;
		const long next = start + 90_000;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TransportStreamSplitter(), OneMinute, async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, start + 3_600, start, null, true), .. TsSamples.TimedPes(TsSamples.AudioPid, start - 900), .. TsSamples.TimedPes(TsSamples.VideoPid, start + 7_200, start + 3_600)]);
			times.Add(session.Now);
			session.Advance(TimeSpan.FromMinutes(1));
			await session.WriteAsync([.. TsSamples.TimedPes(TsSamples.VideoPid, next + 3_600, next, null, true), .. TsSamples.TimedPes(TsSamples.AudioPid, next - 900), .. TsSamples.TimedPes(TsSamples.VideoPid, next + 93_600, next + 90_000, null, true)]);
			times.Add(session.Now);
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files.Select(DescribeTimestamps)).IsEquivalentTo((string[][])[["V:p4500,d900", "A:p0", "V:p8100,d4500"], ["V:p4500,d900", "A:p0", "V:p94500,d90900"]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@50", "1@1010"], CollectionOrdering.Matching);
			await Assert.That((recording.Timeline.GetDuration("0"), recording.Timeline.GetDuration("1"))).IsEqualTo((TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(1010)));
		}
	}

	[Test]
	public async Task TransportStreamStartIgnoresTimesFromBeforeARestartAsync(CancellationToken cancellationToken)
	{
		const long decode = 900_000;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, decode, null, null, true), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 45_000), .. TsSamples.TimedPes(TsSamples.VideoPid, decode - 180_000), .. TsSamples.TimedPes(TsSamples.VideoPid, decode - 135_000)]);
			times.Add(session.Now);
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:p0", "V:p45000", "V:p45000", "V:p90000"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@1000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TransportStreamCountsByDecodeTimeAcrossARestartAsync(CancellationToken cancellationToken)
	{
		const long decode = 900_000;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 7_200, decode, decode - 135_000, true), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 187_200, decode + 180_000, decode + 45_000), .. TsSamples.TimedPes(TsSamples.VideoPid, 3_600, 0, null, true), .. TsSamples.TimedPes(TsSamples.VideoPid, 7_200, 3_600)]);
			times.Add(session.Now);
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:c0,p7200,d0", "V:c45000,p187200,d180000", "V:p183600,d180000", "V:p187200,d183600"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2040"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task FFmpegTransportStreamWithoutSplittingStartsAtTheEarliestPesOfItsFirstUnitAsync(CancellationToken cancellationToken)
	{
		const long decode = 900_000;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TransportStreamSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.WriteAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 3_600, decode, null, true)]);
			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(1));
			await session.WriteAsync([.. TsSamples.TimedPes(TsSamples.AudioPid, decode - 3_600), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 7_200, decode + 3_600)]);
			times.Add(session.Now);
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:p7200,d3600", "A:p0", "V:p10800,d7200"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@40", "0@80"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TransportStreamTimelineThatStartsOverContinuesFromTheLastTimestampAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			foreach ((long decode, long audio) in ((long, long)[])[(900_000, 899_100), (0, 450)])
			{
				await session.SegmentAsync([.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, decode, null, decode, true), .. TsSamples.TimedPes(TsSamples.AudioPid, audio), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 90_000)]);
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(1));
			}
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:c900,p900", "A:p0", "V:p90900", "V:c90900,p90900", "A:p91350", "V:p180900"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@1010", "0@2010"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TsHlsSegmentThatGoesBackLessThanItsLengthContinuesAfterTheWrittenTimeAsync(CancellationToken cancellationToken)
	{
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			foreach (long first in (long[])[900_000, 1_260_000, 1_260_000])
			{
				await session.SegmentAsync(TsSegment(first, first + 180_000));
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(4));
			}
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:c0,p0", "V:p180000", "V:c360000,p360000", "V:p540000", "V:c540000,p540000", "V:p720000"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2000", "0@6000", "0@8000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task TsHlsTimelineFollowsARestartInTheMiddleOfASegmentAsync(CancellationToken cancellationToken)
	{
		const long hour = 3600 * PtsClock.ClockRate;
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), new RecordingSplit.None(), async session =>
		{
			await session.SegmentAsync(TsSegment(hour, hour + 90_000, 0, 90_000));
			times.Add(session.Now);
			session.Advance(TimeSpan.FromSeconds(4));
			await session.SegmentAsync(TsSegment(180_000, 270_000));
			times.Add(session.Now);
		}, cancellationToken);

		byte[] file = await Assert.That(recording.Files).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(DescribeTimestamps(file)).IsEquivalentTo(["V:c0,p0", "V:p90000", "V:p90000", "V:p180000", "V:c270000,p270000", "V:p360000"], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@2000", "0@4000"], CollectionOrdering.Matching);
		}
	}

	[Test]
	[Arguments((byte[])[0, 0], 0, DisplayName = "分片开头有杂字节")]
	[Arguments((byte[])[0x47, 0], 0, DisplayName = "杂字节以 0x47 开头")]
	[Arguments((byte[])[], 100, DisplayName = "分片末尾有不完整的包")]
	public async Task TsHlsSegmentsOffThePacketGridAreRebasedFromTheirOwnFirstPacketAsync(byte[] junk, int trailing, CancellationToken cancellationToken)
	{
		byte[] partial = TsSamples.Packet(TsSamples.AudioPid, false, new byte[184]).AsSpan(0, trailing).ToArray();
		SplitRecording recording = new();
		List<long> times = [];

		await recording.RunAsync(new TsHlsSplitter(), OneMinute, async session =>
		{
			foreach (byte[] segment in (byte[][])[Segment(900_000, true), [.. junk, .. Segment(1_080_000, true), .. partial], Segment(1_260_000, false)])
			{
				await session.SegmentAsync(segment);
				times.Add(session.Now);
				session.Advance(TimeSpan.FromSeconds(1));
			}
		}, cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(recording.Files).IsEquivalentTo((byte[][])[[.. Segment(0, true), .. junk, .. Segment(180_000, true), .. partial, .. Segment(360_000, false)]], CollectionOrdering.Matching);
			await Assert.That(Place(recording, times)).IsEquivalentTo(["0@1000", "0@3000", "0@5000"], CollectionOrdering.Matching);
		}

		static byte[] Segment(long decode, bool isKey)
		{
			return [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, decode, null, decode, isKey), .. TsSamples.TimedPes(TsSamples.VideoPid, decode + 90_000)];
		}
	}

	private static IEnumerable<string> Place(SplitRecording recording, IEnumerable<long> times)
	{
		return times.Select(time => recording.Timeline.TryPlace(time, out TimelinePosition? position) && position is { } placed ? string.Create(CultureInfo.InvariantCulture, $"{placed.Segment}@{placed.Time.TotalMilliseconds}") : "null");
	}

	private static byte[] TsSegment(params long[] pts)
	{
		return [.. TsSamples.Pat(), .. TsSamples.Pmt(), .. TsSamples.TimedPes(TsSamples.VideoPid, pts[0], null, pts[0], true), .. pts.Skip(1).SelectMany(static time => TsSamples.TimedPes(TsSamples.VideoPid, time))];
	}

	// c 是 PCR 的基准值，p 和 d 是 PTS 和 DTS
	private static string[] DescribeTimestamps(byte[] file)
	{
		List<string> packets = [];

		for (int offset = 0; offset + TsSamples.PacketSize <= file.Length; offset += TsSamples.PacketSize)
		{
			ReadOnlySpan<byte> packet = file.AsSpan(offset, TsSamples.PacketSize);
			int pid = (packet[1] & 0x1F) << 8 | packet[2];
			bool hasAdaptation = (packet[3] & 0x20) is not 0;
			ReadOnlySpan<byte> payload = packet.Slice(hasAdaptation ? 5 + packet[4] : 4);

			if ((packet[1] & 0x40) is 0 || !payload.StartsWith((ReadOnlySpan<byte>)[0, 0, 1]))
			{
				continue;
			}

			List<string> fields = [];

			if (hasAdaptation && (packet[5] & 0x10) is not 0)
			{
				fields.Add($"c{(long)packet[6] << 25 | (long)packet[7] << 17 | (long)packet[8] << 9 | (long)packet[9] << 1 | (long)packet[10] >> 7}");
			}

			fields.Add($"p{ReadTimestamp(payload.Slice(9))}");

			if (payload[7] >> 6 is 3)
			{
				fields.Add($"d{ReadTimestamp(payload.Slice(14))}");
			}

			packets.Add($"{(pid is TsSamples.VideoPid ? "V" : "A")}:{string.Join(',', fields)}");
		}

		return [.. packets];

		static long ReadTimestamp(ReadOnlySpan<byte> field)
		{
			return (long)(field[0] >> 1 & 0x07) << 30 | (long)field[1] << 22 | (long)(field[2] >> 1) << 15 | (long)field[3] << 7 | (long)field[4] >> 1;
		}
	}

	private static byte[] Fmp4Init(uint audioTimescale = AudioTimescale)
	{
		return [.. Fmp4Samples.Box("ftyp", "isom"u8.ToArray()), .. Fmp4Samples.Box("moov", [.. Track(Fmp4Samples.VideoTrack, "vide", VideoTimescale), .. Track(Fmp4Samples.AudioTrack, "soun", audioTimescale), .. Fmp4Samples.Box("mvex", [.. Trex(Fmp4Samples.VideoTrack), .. Trex(Fmp4Samples.AudioTrack)])])];

		static byte[] Track(uint trackId, string handler, uint timescale)
		{
			byte[] tkhd = new byte[24];
			BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(12), trackId);
			byte[] mdhd = new byte[24];
			BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12), timescale);
			byte[] hdlr = new byte[24];
			Encoding.ASCII.GetBytes(handler, hdlr.AsSpan(8));
			return Fmp4Samples.Box("trak", [.. Fmp4Samples.Box("tkhd", tkhd), .. Fmp4Samples.Box("mdia", [.. Fmp4Samples.Box("mdhd", mdhd), .. Fmp4Samples.Box("hdlr", hdlr)])]);
		}

		static byte[] Trex(uint trackId)
		{
			byte[] trex = new byte[24];
			BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(4), trackId);
			BinaryPrimitives.WriteUInt32BigEndian(trex.AsSpan(20), Fmp4Samples.SyncFlags);
			return Fmp4Samples.Box("trex", trex);
		}
	}

	// 2 秒的分片
	private static byte[] Fmp4Fragment(ulong video, ulong audio, byte videoVersion = 1)
	{
		return Fmp4MovieFragment(VideoTraf(video, videoVersion, 60), AudioTraf(audio, 0, 96_000));
	}

	private static byte[] Fmp4MovieFragment(params byte[][] trafs)
	{
		return [.. Fmp4Samples.Box("moof", [.. trafs.SelectMany(static traf => traf)]), .. Fmp4Samples.Box("mdat", "[timed]"u8.ToArray())];
	}

	// trun 0x104 带首样本标志和样本时长
	private static byte[] VideoTraf(ulong decodeTime, byte version, uint samples)
	{
		byte[] trun = [.. Words(0x104, samples, Fmp4Samples.SyncFlags), .. Enumerable.Repeat(Words(3000), (int)samples).SelectMany(static word => word)];
		return Fmp4Samples.Box("traf", [.. Fmp4Samples.Box("tfhd", Words(0, Fmp4Samples.VideoTrack)), .. Tfdt(decodeTime, version), .. Fmp4Samples.Box("trun", trun)]);
	}

	private static byte[] AudioTraf(ulong decodeTime, byte version, uint duration)
	{
		return Fmp4Samples.Box("traf", [.. Fmp4Samples.Box("tfhd", Words(0, Fmp4Samples.AudioTrack)), .. Tfdt(decodeTime, version), .. Fmp4Samples.Box("trun", Words(0x100, 1, duration))]);
	}

	// 版本 0 只写低 32 位，超出部分回绕
	private static byte[] Tfdt(ulong decodeTime, byte version)
	{
		byte[] tfdt = new byte[version is 1 ? 12 : 8];
		tfdt[0] = version;

		if (version is 1)
		{
			BinaryPrimitives.WriteUInt64BigEndian(tfdt.AsSpan(4), decodeTime);
		}
		else
		{
			BinaryPrimitives.WriteUInt32BigEndian(tfdt.AsSpan(4), (uint)decodeTime);
		}

		return Fmp4Samples.Box("tfdt", tfdt);
	}

	private static byte[] Words(params uint[] values)
	{
		byte[] bytes = new byte[values.Length * 4];

		for (int index = 0; index < values.Length; ++index)
		{
			BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index * 4), values[index]);
		}

		return bytes;
	}

	private static string[] DescribeDecodeTimes(byte[] file)
	{
		List<string> times = [];

		foreach (ReadOnlyMemory<byte> moof in Boxes(file, "moof"))
		{
			foreach (ReadOnlyMemory<byte> traf in Boxes(moof, "traf"))
			{
				uint trackId = BinaryPrimitives.ReadUInt32BigEndian(Boxes(traf, "tfhd").Single().Span.Slice(4));
				ReadOnlySpan<byte> tfdt = Boxes(traf, "tfdt").Single().Span;
				ulong time = tfdt[0] is 1 ? BinaryPrimitives.ReadUInt64BigEndian(tfdt.Slice(4)) : BinaryPrimitives.ReadUInt32BigEndian(tfdt.Slice(4));
				times.Add($"{(trackId == Fmp4Samples.VideoTrack ? "V" : "A")}{time}");
			}
		}

		return [.. times];

		// 盒子头被改坏时直接报错，避免卡在同一个位置
		static List<ReadOnlyMemory<byte>> Boxes(ReadOnlyMemory<byte> data, string type)
		{
			List<ReadOnlyMemory<byte>> boxes = [];

			for (int offset = 0; offset + 8 <= data.Length;)
			{
				int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Span.Slice(offset));

				if (size < 8 || size > data.Length - offset)
				{
					throw new InvalidDataException($"Box at {offset} has size {size}");
				}

				if (Encoding.ASCII.GetString(data.Span.Slice(offset + 4, 4)) == type)
				{
					boxes.Add(data.Slice(offset + 8, size - 8));
				}

				offset += size;
			}

			return boxes;
		}
	}
}
