using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

internal closed record HybridConversionResult
{
	// RetimedSamples 是解码时间往回走、推到上一个样本之后的样本数
	public sealed record Converted(long WrittenBytes, long DroppedSamples, long RetimedSamples) : HybridConversionResult;

	// 开头的 moov 不是分片的，什么都不做
	public sealed record AlreadyPlain : HybridConversionResult;

	// 结构不支持或新 moov 没通过校验；开头和录下的数据都没动，追加的尾巴截不掉时会留在末尾，文件仍是能播放的分片 MP4
	public sealed record Rejected(string Reason) : HybridConversionResult;
}

// 把录完的分片 MP4 原地改成普通 MP4，媒体数据不搬动也不改写：
// [ftyp][mdat：64 位大小，盖住原来的 init moov 和所有 moof、mdat][放章节文本样本的 mdat][moov：完整的样本表]
// 样本原样引用，AV1 样本里的时间分隔符 OBU 也留着：AV1-ISOBMFF 只是建议不放，解码器都能处理
// 按下面的顺序写，每一步都先落盘，中间任何时候被打断，文件都还是能播放的分片 MP4，对它再调用一次 Convert 能接着做完：
// 1. 截掉上次中断留下的尾巴
// 2. 在末尾追加章节 mdat 和一个 free，free 的内容就是新 moov；读回来校验，不通过就截掉
// 3. 把 free 的类型改成 moov；此时开头的分片 moov 仍然有效，读取方用第一个 moov（FFmpeg 跳过后面重复的）
// 4. 一次写入 [0, 原 moov 开头 + 16)：大小不变的新 ftyp 和 64 位的 mdat 头；这块在第一个扇区里才不会只写一半，所以 FragmentedFileReader 不接受超出的文件
// 只有一种中断续不完：录制时最后一个盒子没写完，第 2 步的追加又只落盘了一半，尾巴不在盒子边界上，头部备份也没写到，认不出来，再转换会把它当成录下的数据
internal sealed class HybridMp4Converter
{
	private readonly SafeFileHandle _handle;
	private readonly FragmentedLayout _layout;
	private readonly IReadOnlyList<MediaChapter> _chapters;
	private readonly byte[] _header;
	private AppendedMovie _appended;

	internal HybridMp4Converter(SafeFileHandle handle, FragmentedLayout layout, IReadOnlyList<MediaChapter> chapters)
	{
		Debug.Assert(layout.MovieStart + HybridMovie.MediaDataHeaderSize <= HybridMovie.MaxHeadSize);

		_handle = handle;
		_layout = layout;
		_chapters = chapters;
		_header = new byte[layout.MovieStart + HybridMovie.MediaDataHeaderSize];
		RandomAccess.Read(handle, _header, 0);
	}

	// 文件被别的程序占着写不了时抛出 IOException
	public static HybridConversionResult Convert(string path, IReadOnlyList<MediaChapter> chapters)
	{
		using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);

		try
		{
			return FragmentedFileReader.Read(handle) switch
			{
				FragmentedFile.Plain => new HybridConversionResult.AlreadyPlain(),
				FragmentedFile.Fragmented { Layout: var layout } => new HybridMp4Converter(handle, layout, chapters).Run()
			};
		}
		catch (UnsupportedLayoutException exception)
		{
			return new HybridConversionResult.Rejected(exception.Message);
		}
	}

	private HybridConversionResult Run()
	{
		try
		{
			Truncate();
			Append();

			if (Verify() is { } failure)
			{
				TryTruncate();
				return new HybridConversionResult.Rejected(failure);
			}

			Flip();
		}
		catch (UnsupportedLayoutException exception)
		{
			TryTruncate();
			return new HybridConversionResult.Rejected(exception.Message);
		}
		catch
		{
			TryTruncate();
			throw;
		}

		// 开头一旦开始改，就不能再截掉末尾的新 moov
		Commit();
		return new HybridConversionResult.Converted(_appended.End - _layout.DataEnd + _header.Length, _layout.DroppedSamples, _layout.RetimedSamples);
	}

	internal void Truncate()
	{
		if (RandomAccess.GetLength(_handle) != _layout.DataEnd)
		{
			RandomAccess.SetLength(_handle, _layout.DataEnd);
			RandomAccess.FlushToDisk(_handle);
		}
	}

	internal void Append()
	{
		_appended = HybridMovieWriter.Write(_handle, _layout, _chapters, _header);
		RandomAccess.FlushToDisk(_handle);
	}

	internal string? Verify()
	{
		return HybridMovieVerifier.Verify(_handle, _layout, _appended);
	}

	internal void Flip()
	{
		RandomAccess.Write(_handle, "moov"u8, _appended.MovieStart + 4);
		RandomAccess.FlushToDisk(_handle);
	}

	private void Commit()
	{
		Span<byte> header = stackalloc byte[HybridMovie.MaxHeadSize].Slice(0, _header.Length);
		_header.CopyTo(header);
		WriteFileType(header.Slice(0, _layout.FileTypeSize));

		Span<byte> mediaData = header.Slice((int)_layout.MovieStart);
		BinaryPrimitives.WriteUInt32BigEndian(mediaData, 1);
		BinaryPrimitives.WriteUInt32BigEndian(mediaData.Slice(4), BoxType.Mdat);
		BinaryPrimitives.WriteUInt64BigEndian(mediaData.Slice(8), (ulong)(_layout.DataEnd - _layout.MovieStart));

		RandomAccess.Write(_handle, header, 0);
		RandomAccess.FlushToDisk(_handle);
	}

	private void TryTruncate()
	{
		try
		{
			Truncate();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 截不掉时文件仍是能播放的分片 MP4，再转换一次时第一步会截掉它；这里留着原来的拒绝原因或异常
		}
	}

	// 大小不变；原来的 dash 品牌对普通 MP4 不成立，要换掉。主品牌用最通用的 isom，次版本号只是参考
	// 兼容品牌依次是 isom、AV1-ISOBMFF 要求的 av01、roll 样本组所属的 iso2、AVC 的 avc1、MP4 文件格式的 mp41，按编码取舍，放不下就截短，有空余就重复 isom
	private void WriteFileType(Span<byte> ftyp)
	{
		bool isAv1 = false;
		bool isAvc = false;

		foreach (TrackIndex track in _layout.Tracks)
		{
			if (track is { Kind: TrackKind.Video, SampleCount: > 0 })
			{
				isAv1 |= track.SampleEntryType is BoxType.Av01;
				isAvc |= track.SampleEntryType is BoxType.Avc1 or BoxType.Avc3;
			}
		}

		Span<uint> brands = stackalloc uint[5];
		int count = 0;
		brands[count++] = Brand.Isom;

		if (isAv1)
		{
			brands[count++] = Brand.Av01;
		}

		brands[count++] = Brand.Iso2;

		if (isAvc)
		{
			brands[count++] = Brand.Avc1;
		}

		brands[count++] = Brand.Mp41;

		Span<byte> content = ftyp.Slice(8);
		BinaryPrimitives.WriteUInt32BigEndian(content, Brand.Isom);
		BinaryPrimitives.WriteUInt32BigEndian(content.Slice(4), 0x200);
		Span<byte> compatible = content.Slice(8);

		for (int slot = 0; slot < compatible.Length / 4; ++slot)
		{
			BinaryPrimitives.WriteUInt32BigEndian(compatible.Slice(slot * 4), slot < count ? brands[slot] : Brand.Isom);
		}
	}

	// av01、avc1 品牌和同名的样本项类型是两回事，不用 BoxType 里的
	private static class Brand
	{
		public const uint Isom = 'i' << 24 | 's' << 16 | 'o' << 8 | 'm';
		public const uint Av01 = 'a' << 24 | 'v' << 16 | '0' << 8 | '1';
		public const uint Iso2 = 'i' << 24 | 's' << 16 | 'o' << 8 | '2';
		public const uint Avc1 = 'a' << 24 | 'v' << 16 | 'c' << 8 | '1';
		public const uint Mp41 = 'm' << 24 | 'p' << 16 | '4' << 8 | '1';
	}
}
