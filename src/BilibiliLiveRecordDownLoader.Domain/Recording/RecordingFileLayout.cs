using System.Buffers;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public static class RecordingFileLayout
{
	private const string Mp4Extension = ".mp4";

	public const string DanmakuExtension = ".xml";

	public const string SubtitleExtension = ".ass";

	public const string RawDanmakuExtension = ".danmaku.jsonl";

	// fMP4 录制文件本身就是 MP4，靠后缀和转封装后的文件区分
	private const string Fmp4Suffix = "_fmp4";

	private const string Fmp4RecordingSuffix = Fmp4Suffix + Mp4Extension;

	// ext4 的文件名最多 255 字节，资源管理器只认 260 个字符以内的完整路径；截短后时间、序号和扩展名都还放得下
	private const int MaxTitleBytes = 120;

	private static readonly string[] RecordingSuffixes = [".flv", ".ts", Fmp4RecordingSuffix];

	// 同一主干的录制文件转封装后都会落到同一个 .mp4
	// 标题可能以 _fmp4 结尾，主干加上 _fmp4 的录制文件转封装后会落到本主干的 fMP4 文件上
	// 已有弹幕文件的主干再分给视频会混入两次录制的弹幕
	private static readonly string[] StemSuffixes = [.. RecordingSuffixes, Mp4Extension, .. RecordingSuffixes.Select(static suffix => Fmp4Suffix + suffix), DanmakuExtension, SubtitleExtension, RawDanmakuExtension];

	// 顺序就是找同主干视频时的优先顺序
	private static readonly string[] VideoSuffixes = [Mp4Extension, Fmp4RecordingSuffix, ".flv", ".ts"];

	// 录制目录可能在网络盘上，Linux 下也按 Windows 的规则替换
	private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create("\"*/:<>?\\|");

	public static string GetRoomDirectory(string recordDirectory, long roomId)
	{
		return Path.Combine(recordDirectory, roomId.ToString(CultureInfo.InvariantCulture));
	}

	public static string GetRecordingStem(string recordDirectory, long roomId, DateTimeOffset startTime, string? title, int sequence = 0)
	{
		StringBuilder fileName = new(startTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

		if (ToFileNamePart(title) is { } titlePart)
		{
			fileName.Append('_').Append(titlePart);
		}

		if (sequence > 0)
		{
			fileName.Append('_').Append(sequence.ToString(CultureInfo.InvariantCulture));
		}

		return Path.Combine(GetRoomDirectory(recordDirectory, roomId), fileName.ToString());
	}

	public static string GetRecordingPath(string stem, StreamFormat format)
	{
		return stem + format switch
		{
			StreamFormat.Flv => ".flv",
			StreamFormat.Ts => ".ts",
			StreamFormat.Fmp4 => Fmp4RecordingSuffix
		};
	}

	public static bool IsStemAvailable(string stem, Func<string, bool> fileExists)
	{
		return !Array.Exists(StemSuffixes, suffix => fileExists(stem + suffix));
	}

	// 只用于录制文件、弹幕文件和原始消息文件：转封装之后的 .mp4 主干以 _fmp4 结尾时会被误认成 fMP4 录制文件
	public static string GetStem(string path)
	{
		foreach (string suffix in (ReadOnlySpan<string>)[Fmp4RecordingSuffix, RawDanmakuExtension])
		{
			if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				return path[..^suffix.Length];
			}
		}

		return Path.ChangeExtension(path, null);
	}

	public static IEnumerable<string> GetVideoPaths(string stem)
	{
		return VideoSuffixes.Select(suffix => stem + suffix);
	}

	public static bool IsFmp4Recording(string recordingPath)
	{
		return recordingPath.EndsWith(Fmp4RecordingSuffix, StringComparison.OrdinalIgnoreCase);
	}

	public static string? GetMp4ConversionPath(string recordingPath)
	{
		if (IsFmp4Recording(recordingPath))
		{
			return string.Concat(recordingPath.AsSpan(0, recordingPath.Length - Fmp4RecordingSuffix.Length), Mp4Extension);
		}

		return Path.GetExtension(recordingPath.AsSpan()).Equals(Mp4Extension, StringComparison.OrdinalIgnoreCase) ? null : Path.ChangeExtension(recordingPath, Mp4Extension);
	}

	private static string? ToFileNamePart(string? title)
	{
		ReadOnlySpan<char> trimmed = title.AsSpan().Trim();

		if (trimmed.IsEmpty)
		{
			return null;
		}

		// 控制字符和非法字符都在 BMP 内，孤立的代理项换成同样占一个 char 的 U+FFFD，所以长度不变
		string cleaned = string.Create(trimmed.Length, trimmed, static (destination, source) =>
		{
			while (!source.IsEmpty)
			{
				_ = Rune.DecodeFromUtf16(source, out Rune rune, out int consumed);

				if (Rune.IsControl(rune) || rune.IsBmp && InvalidFileNameChars.Contains((char)rune.Value))
				{
					rune = new Rune('_');
				}

				int written = rune.EncodeToUtf16(destination);
				source = source.Slice(consumed);
				destination = destination.Slice(written);
			}
		});

		int length = 0;
		int bytes = 0;

		while (length < cleaned.Length)
		{
			int next = StringInfo.GetNextTextElementLength(cleaned, length);
			bytes += Encoding.UTF8.GetByteCount(cleaned.AsSpan(length, next));

			if (bytes > MaxTitleBytes)
			{
				break;
			}

			length += next;
		}

		string part = cleaned[..length].TrimEnd();
		return part.Length > 0 ? part : null;
	}
}
