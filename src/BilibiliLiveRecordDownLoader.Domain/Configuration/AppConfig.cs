namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public record AppConfig
{
	public string? Language { get; set; }

	public AppTheme Theme { get; set; }

	public bool AutoCheckUpdate { get; set; } = true;

	public bool IncludePreRelease { get; set; }

	public double WindowWidth { get; set; } = 1280;

	public double WindowHeight { get; set; } = 720;

	public RoomSortOrder RoomSortOrder { get; set; }

	public string RecordDirectory { get; set; } = string.Empty;

	public string? FFmpegPath { get; set; }

	public string? UserAgent { get; set; }

	public string? Cookie { get; set; }

	public bool UseSystemProxy { get; set; }

	public RecorderType RecorderType { get; set; }

	public List<StreamCodec> CodecPriority { get; set; } = [];

	public List<StreamFormat> FormatPriority { get; set; } = [];

	public StreamHostSelection StreamHostSelection { get; set; }

	public bool ConvertToMp4 { get; set; }

	public bool DeleteAfterConvert { get; set; }

	public bool NotifyOnRecordingError { get; set; } = true;

	public bool NotifyOnTitleChange { get; set; }

	public List<RoomConfig> Rooms { get; set; } = [];

	public static AppConfig CreateDefault()
	{
		AppConfig config = new();
		config.ApplyDefaults();
		return config;
	}

	public void ApplyDefaults()
	{
		if (string.IsNullOrWhiteSpace(RecordDirectory))
		{
			RecordDirectory = ConfigurationConsts.DefaultRecordDirectory;
		}

		CodecPriority = StreamPriority.Normalize(CodecPriority, StreamPriority.DefaultCodecs);
		FormatPriority = StreamPriority.Normalize(FormatPriority, StreamPriority.DefaultFormats);
		Rooms = [.. Rooms.Where(static room => room.RoomId > 0).DistinctBy(static room => room.RoomId)];
		Rooms.ForEach(static room => room.ApplyDefaults());
	}
}
