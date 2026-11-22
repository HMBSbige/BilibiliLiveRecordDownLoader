namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public sealed record RecordingProfile
(
	string RecordDirectory,
	RecorderType RecorderType,
	StreamQuality Quality,
	bool RequestTrueOriginal,
	bool TryOriginalWhenRestricted,
	IReadOnlyList<StreamCodec> CodecPriority,
	IReadOnlyList<StreamFormat> FormatPriority,
	StreamHostSelection HostSelection,
	bool ConvertToMp4,
	bool DeleteAfterConvert,
	TimeSpan StreamReconnectDelay,
	TimeSpan StreamConnectTimeout,
	TimeSpan StreamIdleTimeout
)
{
	// 真原画是原画这一档没被 B 站重新压过的版本，选 4K、杜比等其他画质时照常请求
	public long RequestQuality => (Quality, RequestTrueOriginal) is (StreamQuality.Original, true) ? StreamQualityConsts.TrueOriginal : (long)Quality;

	public static RecordingProfile Create(AppConfig app, RoomConfig room)
	{
		return new RecordingProfile
		(
			app.RecordDirectory,
			room.RecorderType ?? app.RecorderType,
			room.Quality,
			app.RequestTrueOriginal,
			app.TryOriginalWhenRestricted,
			StreamPriority.Normalize(room.CodecPriority ?? app.CodecPriority, StreamPriority.DefaultCodecs),
			StreamPriority.Normalize(room.FormatPriority ?? app.FormatPriority, StreamPriority.DefaultFormats),
			app.StreamHostSelection,
			room.ConvertToMp4 ?? app.ConvertToMp4,
			room.DeleteAfterConvert ?? app.DeleteAfterConvert,
			room.StreamReconnectDelay,
			room.StreamConnectTimeout,
			room.StreamIdleTimeout
		);
	}
}
