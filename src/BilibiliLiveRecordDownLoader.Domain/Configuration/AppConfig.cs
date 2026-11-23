namespace BilibiliLiveRecordDownLoader.Domain.Configuration;

public sealed record AppConfig
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

	public IReadOnlyList<StreamCodec> CodecPriority { get; set; } = [];

	public IReadOnlyList<StreamFormat> FormatPriority { get; set; } = [];

	public StreamHostSelection StreamHostSelection { get; set; }

	public bool ConvertToMp4 { get; set; }

	public bool DeleteAfterConvert { get; set; }

	public RecordingSplitMode SplitMode { get; set; }

	public TimeSpan SplitDuration { get; set; } = RecordingSplitDefaults.Duration;

	public long SplitSizeMebibytes { get; set; } = RecordingSplitDefaults.SizeMebibytes;

	public bool SplitOnTitleChange { get; set; }

	public bool IncludeTitleInFileName { get; set; } = true;

	public bool NotifyOnRecordingError { get; set; } = true;

	public bool NotifyOnTitleChange { get; set; }

	public bool RemoveFillerData { get; set; }

	public bool RequestTrueOriginal { get; set; }

	public bool UseMasterPlaylistWhenRestricted { get; set; } = true;

	public bool RecordSuperChats { get; set; } = true;

	public DanmakuGiftMode GiftMode { get; set; }

	public bool RecordGuards { get; set; } = true;

	// 正数让弹幕更晚出现
	public TimeSpan DanmakuOffset { get; set; }

	public bool GenerateDanmakuSubtitle { get; set; } = true;

	public bool ShowPaidMessagesInSubtitle { get; set; } = true;

	public bool SaveRawDanmaku { get; set; }

	public bool WriteChaptersOnConvert { get; set; }

	public IReadOnlyList<RoomConfig> Rooms { get; set; } = [];

	public static AppConfig CreateDefault()
	{
		AppConfig config = new();
		config.ApplyDefaults();
		return config;
	}

	public void ApplyDefaults()
	{
		Language = ConfigValues.NormalizeText(Language);
		RecordDirectory = ConfigValues.NormalizeRecordDirectory(RecordDirectory);
		FFmpegPath = ConfigValues.NormalizeText(FFmpegPath);
		UserAgent = ConfigValues.NormalizeText(UserAgent);
		Cookie = ConfigValues.NormalizeText(Cookie);
		// 枚举在配置里也能写成数字；按值分支的地方不认识未定义的值
		Theme = DefinedOrDefault(Theme);
		RoomSortOrder = DefinedOrDefault(RoomSortOrder);
		RecorderType = DefinedOrDefault(RecorderType);
		StreamHostSelection = DefinedOrDefault(StreamHostSelection);
		SplitMode = DefinedOrDefault(SplitMode);
		GiftMode = DefinedOrDefault(GiftMode);
		CodecPriority = StreamPriority.Normalize(CodecPriority, StreamPriority.DefaultCodecs);
		FormatPriority = StreamPriority.Normalize(FormatPriority, StreamPriority.DefaultFormats);
		SplitDuration = ConfigValues.ClampSplitDuration(SplitDuration);
		SplitSizeMebibytes = ConfigValues.ClampSplitSize(SplitSizeMebibytes);
		DanmakuOffset = ConfigValues.ClampDanmakuOffset(DanmakuOffset);
		// 手改的配置里 Rooms 或其中的元素可能是 null
		// ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
		Rooms = [.. (Rooms ?? []).Where(static room => room is { RoomId: > 0 }).DistinctBy(static room => room.RoomId).Select(static room => room.Normalize())];
	}

	private static T DefinedOrDefault<T>(T value) where T : struct, Enum
	{
		return Enum.IsDefined(value) ? value : default;
	}
}
