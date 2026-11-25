using BilibiliLiveRecordDownLoader.Domain.Shared;

namespace BilibiliLiveRecordDownLoader.Application.Contracts.Settings;

public sealed record AppSettingsDto
{
	public required string? Language { get; init; }

	public required AppTheme Theme { get; init; }

	public required bool AutoCheckUpdate { get; init; }

	public required bool IncludePreRelease { get; init; }

	public required double WindowWidth { get; init; }

	public required double WindowHeight { get; init; }

	public required RoomSortOrder RoomSortOrder { get; init; }

	public required string RecordDirectory { get; init; }

	public required string? FFmpegPath { get; init; }

	public required string? UserAgent { get; init; }

	public required bool AutoRenewCookie { get; init; }

	public required bool UseSystemProxy { get; init; }

	public required RecorderType RecorderType { get; init; }

	public required IReadOnlyList<StreamCodec> CodecPriority { get; init; }

	public required IReadOnlyList<StreamFormat> FormatPriority { get; init; }

	public required StreamHostSelection StreamHostSelection { get; init; }

	public required bool ConvertToMp4 { get; init; }

	public required bool DeleteAfterConvert { get; init; }

	public required RecordingSplitMode SplitMode { get; init; }

	public required TimeSpan SplitDuration { get; init; }

	public required long SplitSizeMebibytes { get; init; }

	public required bool SplitOnTitleChange { get; init; }

	public required bool IncludeTitleInFileName { get; init; }

	public required bool NotifyOnRecordingError { get; init; }

	public required bool NotifyOnTitleChange { get; init; }

	public required bool RemoveFillerData { get; init; }

	public required bool UseMasterPlaylistWhenRestricted { get; init; }

	public required bool RecordSuperChats { get; init; }

	public required DanmakuGiftMode GiftMode { get; init; }

	public required bool RecordGuards { get; init; }

	// 正数让弹幕更晚出现
	public required TimeSpan DanmakuOffset { get; init; }

	public required bool GenerateDanmakuSubtitle { get; init; }

	public required bool ShowPaidMessagesInSubtitle { get; init; }

	public required bool SaveRawDanmaku { get; init; }

	public required bool WriteChaptersOnConvert { get; init; }
}
