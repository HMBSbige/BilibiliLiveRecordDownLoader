namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public sealed record RecordingProfile
(
	string RecordDirectory,
	RecorderType RecorderType,
	StreamQuality Quality,
	bool UseMasterPlaylistWhenRestricted,
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
	public static RecordingProfile Create(AppConfig app, RoomConfig room)
	{
		return new RecordingProfile
		(
			app.RecordDirectory,
			room.RecorderType ?? app.RecorderType,
			room.Quality,
			app.UseMasterPlaylistWhenRestricted,
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
