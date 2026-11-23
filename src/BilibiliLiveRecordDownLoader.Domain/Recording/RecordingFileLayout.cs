using System.Buffers;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public static class RecordingFileLayout
{
	private const string Mp4Extension = ".mp4";

	public const string DanmakuExtension = ".xml";

	public const string SubtitleExtension = ".ass";

	// 一次录制或仅录弹幕从开始到停止共用一个，不随分段换，以第一个文件的主干命名
	public const string RawDanmakuExtension = ".danmaku.jsonl";

	// 内置录制的 fMP4 本身就是 MP4，用后缀区分转封装前后的文件
	private const string Fmp4Suffix = "_fmp4";

	// ext4 的文件名最多 255 字节，资源管理器只认 260 个字符以内的完整路径；截短后时间、序号和扩展名都还放得下
	private const int MaxTitleBytes = 120;

	private static readonly string[] RecordingSuffixes = [".flv", ".ts", Fmp4Suffix + Mp4Extension];

	// 同一主干的录制文件转封装后都会落到同一个 .mp4，主干上已有任意一种文件就不能再用
	// 标题可能以 _fmp4 结尾，主干加上 _fmp4 的录制文件转封装后会落到本主干的 fMP4 文件上
	// 已有弹幕文件的主干再分给视频会把两次录制的弹幕混在一起
	private static readonly string[] StemSuffixes = [.. RecordingSuffixes, Mp4Extension, .. RecordingSuffixes.Select(static suffix => Fmp4Suffix + suffix), DanmakuExtension, SubtitleExtension, RawDanmakuExtension];

	// 顺序就是找同主干视频时的优先顺序
	private static readonly string[] VideoSuffixes = [Mp4Extension, Fmp4Suffix + Mp4Extension, ".flv", ".ts"];

	// 录制目录可能在网络盘上，Linux 下也按 Windows 的规则替换
	private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create("\"*/:<>?\\|");

	public static string GetRoomDirectory(string recordDirectory, long roomId)
	{
		return Path.Combine(recordDirectory, roomId.ToString(CultureInfo.InvariantCulture));
	}

	// 时间在最前面，按名称排序就是录制的先后
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

	public static string GetRecordingPath(string stem, StreamFormat format, RecorderType recorderType)
	{
		string extension = (recorderType, format) switch
		{
			(RecorderType.FFmpeg, _) => ".ts",
			(_, StreamFormat.Flv) => ".flv",
			(_, StreamFormat.Fmp4) => Fmp4Suffix + Mp4Extension,
			_ => ".ts"
		};

		return stem + extension;
	}

	public static bool IsStemAvailable(string stem, Func<string, bool> fileExists)
	{
		return !Array.Exists(StemSuffixes, suffix => fileExists(stem + suffix));
	}

	// 只用于录制文件、弹幕文件和原始消息文件：转封装之后的 .mp4 主干以 _fmp4 结尾时会被误认成 fMP4 录制文件
	public static string GetStem(string path)
	{
		foreach (string suffix in (ReadOnlySpan<string>)[Fmp4Suffix + Mp4Extension, RawDanmakuExtension])
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

	public static string? GetMp4ConversionPath(string recordingPath)
	{
		if (!Path.GetExtension(recordingPath).Equals(Mp4Extension, StringComparison.OrdinalIgnoreCase))
		{
			return Path.ChangeExtension(recordingPath, Mp4Extension);
		}

		string withoutExtension = Path.ChangeExtension(recordingPath, null);

		return withoutExtension.EndsWith(Fmp4Suffix, StringComparison.OrdinalIgnoreCase)
			? withoutExtension[..^Fmp4Suffix.Length] + Mp4Extension
			: null;
	}

	private static string? ToFileNamePart(string? title)
	{
		if (string.IsNullOrWhiteSpace(title))
		{
			return null;
		}

		string cleaned = string.Concat(title.Trim().EnumerateRunes().Select(static rune => Rune.IsControl(rune) || (rune.IsBmp && InvalidFileNameChars.Contains((char)rune.Value)) ? "_" : rune.ToString()));

		// 按完整的字符截断，不把 emoji 拆开
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
