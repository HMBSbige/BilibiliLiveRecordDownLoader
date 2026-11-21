namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public static class RecordingFileLayout
{
	private const string Mp4Extension = ".mp4";

	// 内置录制的 fMP4 本身就是 MP4，用后缀区分转封装前后的文件
	private const string Fmp4Suffix = "_fmp4";

	// 同一主干的录制文件转封装后都会落到同一个 .mp4，主干上已有任意一种文件就不能再用
	private static readonly string[] StemSuffixes = [".flv", ".ts", Fmp4Suffix + Mp4Extension, Mp4Extension];

	public static string GetRoomDirectory(string recordDirectory, long roomId)
	{
		return Path.Combine(recordDirectory, roomId.ToString(CultureInfo.InvariantCulture));
	}

	public static string GetRecordingStem(string recordDirectory, long roomId, DateTimeOffset startTime, int sequence = 0)
	{
		string fileName = startTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
			+ (sequence > 0 ? "_" + sequence.ToString(CultureInfo.InvariantCulture) : string.Empty);
		return Path.Combine(GetRoomDirectory(recordDirectory, roomId), fileName);
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
		ArgumentNullException.ThrowIfNull(fileExists);

		return !StemSuffixes.Any(suffix => fileExists(stem + suffix));
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
}
