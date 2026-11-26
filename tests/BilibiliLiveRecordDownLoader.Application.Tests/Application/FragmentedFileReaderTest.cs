using BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

// 损坏、不支持或结构少见的分片 MP4，以及不用转换的普通 MP4
public sealed class FragmentedFileReaderTest
{
	[Test]
	[Arguments(Damage.TrunWithoutDataOffset, "truncated trun")]
	[Arguments(Damage.DataBeforeTheFile, "data before the file")]
	[Arguments(Damage.TfhdWithoutFlags, "truncated tfhd")]
	[Arguments(Damage.CountlessRun, "more than 268435456 samples")]
	[Arguments(Damage.CountlessRuns, "more than 268435456 samples")]
	[Arguments(Damage.VideoWithoutKeyFrames, "no sample to keep")]
	[Arguments(Damage.AudioHiddenByTheEditList, "no sample to present")]
	[Arguments(Damage.DwellEdit, "edit list that cannot be applied")]
	public async Task UnsupportedRecordingIsRejectedAndLeftAsItWasAsync(Damage damage, string reason)
	{
		using TestDirectory directory = new();
		byte[] original = Create(damage);
		string path = directory.WriteAllBytes(HybridSamples.FileName, original);

		HybridConversionResult result = HybridMp4Converter.Convert(path, []);

		using (Assert.Multiple())
		{
			await Assert.That(result).IsTypeOf<HybridConversionResult.Rejected>().And.Member(static rejected => rejected.Reason, actual => actual.Contains(reason, StringComparison.Ordinal));
			await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(original, CollectionOrdering.Matching);
		}
	}

	// 没有 tfdt 的分片接着前一个分片排，第一个从媒体时间 0 开始，和带 tfdt 的一样按 init 段的编辑列表换算：音频开头的编码延迟照样不显示
	[Test]
	public async Task FragmentsWithoutTfdtFollowTheInitEditListAsync()
	{
		using TestDirectory directory = new();
		byte[] original =
		[
			.. HybridSamples.Init(audioEditList: HybridSamples.EditList(1024)),
			.. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(30)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(42))),
			.. HybridSamples.Fragment(2, new RunSpec(HybridSamples.VideoTrack, 90_000, HybridSamples.Video(30, firstIndex: 30)), new RunSpec(HybridSamples.AudioTrack, 42 * 1024, HybridSamples.Audio(47)))
		];
		string expectedPath = directory.WriteAllBytes("expected_fmp4.mp4", original);
		string path = directory.WriteAllBytes(HybridSamples.FileName, Rename(original, "tfdt"u8, "skip"u8));

		HybridMp4Converter.Convert(expectedPath, []);
		HybridMp4Converter.Convert(path, []);

		await Assert.That(Mp4Inspector.ReadMovie(await File.ReadAllBytesAsync(path))).IsEquivalentTo(Mp4Inspector.ReadMovie(await File.ReadAllBytesAsync(expectedPath)), CollectionOrdering.Matching);
	}

	// 转换过的长录制，样本表能有几十 MB；moov 下没有 mvex 就是普通 MP4，不用把 moov 读进来
	[Test]
	public async Task PlainMovieOfAnyLengthIsLeftAloneAsync()
	{
		using TestDirectory directory = new();
		const int movieSize = 65 << 20;
		byte[] movieHeader = HybridSamples.FullBox("mvhd", 0, 0, new byte[96]);
		byte[] head = [.. HybridSamples.FileType(), .. Fmp4Samples.Words(movieSize), .. "moov"u8, .. movieHeader, .. Fmp4Samples.Words((uint)(movieSize - 8 - movieHeader.Length)), .. "free"u8];
		string path = directory.WriteAllBytes("20260102_030405.mp4", head);

		using (SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite))
		{
			RandomAccess.SetLength(handle, HybridSamples.FileType().Length + movieSize);
		}

		await Assert.That(HybridMp4Converter.Convert(path, [])).IsTypeOf<HybridConversionResult.AlreadyPlain>();
	}

	public enum Damage
	{
		TrunWithoutDataOffset,
		DataBeforeTheFile,
		TfhdWithoutFlags,
		CountlessRun,
		CountlessRuns,
		VideoWithoutKeyFrames,
		AudioHiddenByTheEditList,
		DwellEdit
	}

	private static byte[] Create(Damage damage)
	{
		byte[] init = HybridSamples.Init(hasAudio: false);

		return damage switch
		{
			// trun 的标志说带 data_offset，内容却在样本数之后就结束了
			Damage.TrunWithoutDataOffset => WithFragment(init, TrackFragmentHeader(0x02_0000), TrackRun(0x00_0001, Fmp4Samples.Words(0))),

			// data_offset 以 moof 为基准，第一个样本从文件开头之前一个字节开始
			Damage.DataBeforeTheFile => WithFragment(init, TrackFragmentHeader(0x02_0000), TrackRun(0x00_0201, Fmp4Samples.Words(1, unchecked((uint)-(init.Length + 1)), 16))),

			// tfhd 短得连标志都没有，又恰好有一条 track_ID 为 0 的轨道
			Damage.TfhdWithoutFlags => WithFragment(WithTrackIdZero(init), Fmp4Samples.Box("tfhd", [0, 0]), []),

			Damage.CountlessRun => CountlessRuns(init, uint.MaxValue),

			// 每个 trun 都不超过样本上限 2²⁸，加起来超过
			Damage.CountlessRuns => CountlessRuns(init, 1, 1 << 28),

			// 全是非关键帧，转换后整条视频轨就没了
			Damage.VideoWithoutKeyFrames => [.. HybridSamples.Init(), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(30, keyFrameInterval: 1000, firstIndex: 1)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(42)))],

			// 四帧音频都在编辑列表跳过的十帧里，没有要显示的
			Damage.AudioHiddenByTheEditList => [.. HybridSamples.Init(audioEditList: HybridSamples.EditList(10 * 1024)), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(30)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(4)))],

			// 速率为 0 的媒体编辑停在一帧上，套不到分片上
			Damage.DwellEdit => [.. HybridSamples.Init(audioEditList: HybridSamples.EditList(1024, rate: 0)), .. HybridSamples.Fragment(1, new RunSpec(HybridSamples.VideoTrack, 0, HybridSamples.Video(30)), new RunSpec(HybridSamples.AudioTrack, 0, HybridSamples.Audio(42)))],

			_ => throw new UnreachableException()
		};
	}

	// 没有逐样本字段的 trun 不占空间，样本数却可以很大；每个样本 1 字节，从 mdat 开头开始，超出文件的都会丢掉
	private static byte[] CountlessRuns(byte[] init, params uint[] counts)
	{
		byte[] header = TrackFragmentHeader(0x02_0010, Fmp4Samples.Words(1));
		int movieFragmentSize = MovieFragment(header, Runs(0)).Length;
		return WithFragment(init, header, Runs((uint)movieFragmentSize + 8));

		// 只有第一个 trun 带 data_offset，后面的数据接着前一个
		byte[] Runs(uint dataOffset)
		{
			return [.. counts.SelectMany((count, index) => index is 0 ? TrackRun(0x00_0001, Fmp4Samples.Words(count, dataOffset)) : TrackRun(0, Fmp4Samples.Words(count)))];
		}
	}

	// tkhd 的 track_ID 在版本、标志和两个 32 位时间之后
	private static byte[] WithTrackIdZero(byte[] init)
	{
		byte[] patched = [.. init];
		BinaryPrimitives.WriteUInt32BigEndian(patched.AsSpan(patched.AsSpan().IndexOf("tkhd"u8) + 16), 0);
		return patched;
	}

	private static byte[] TrackFragmentHeader(uint flags, byte[]? fields = null)
	{
		return HybridSamples.FullBox("tfhd", 0, flags, [.. Fmp4Samples.Words(HybridSamples.VideoTrack), .. fields ?? []]);
	}

	private static byte[] TrackRun(uint flags, byte[] content)
	{
		return HybridSamples.FullBox("trun", 0, flags, content);
	}

	private static byte[] MovieFragment(byte[] header, byte[] run)
	{
		return Fmp4Samples.Box("moof", [.. HybridSamples.FullBox("mfhd", 0, 0, Fmp4Samples.Words(1)), .. Fmp4Samples.Box("traf", [.. header, .. run])]);
	}

	private static byte[] WithFragment(byte[] init, byte[] header, byte[] run)
	{
		return [.. init, .. MovieFragment(header, run), .. Fmp4Samples.Box("mdat", new byte[64])];
	}

	private static byte[] Rename(byte[] file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> replacement)
	{
		byte[] renamed = [.. file];
		Span<byte> rest = renamed;

		for (int index; (index = rest.IndexOf(type)) >= 0; rest = rest.Slice(index + type.Length))
		{
			replacement.CopyTo(rest.Slice(index));
		}

		return renamed;
	}
}
