namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public static class RecordingFileLayout
{
	private const string Mp4Extension = ".mp4";

	// 内置录制的 fMP4 本身就是 MP4，用后缀区分转封装前后的文件
	private const string Fmp4Suffix = "_fmp4";

	public static string GetRoomDirectory(string recordDirectory, long roomId)
	{
		return Path.Combine(recordDirectory, roomId.ToString(CultureInfo.InvariantCulture));
	}

	public static string CreateRecordingPath(string recordDirectory, long roomId, DateTimeOffset startTime, StreamFormat format, RecorderType recorderType, int sequence = 0)
	{
		string extension = (recorderType, format) switch
		{
			(RecorderType.FFmpeg, _) => ".ts",
			(_, StreamFormat.Flv) => ".flv",
			(_, StreamFormat.Fmp4) => Fmp4Suffix + Mp4Extension,
			_ => ".ts"
		};

		string fileName = startTime.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
			+ (sequence > 0 ? "_" + sequence.ToString(CultureInfo.InvariantCulture) : string.Empty)
			+ extension;
		return Path.Combine(GetRoomDirectory(recordDirectory, roomId), fileName);
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
