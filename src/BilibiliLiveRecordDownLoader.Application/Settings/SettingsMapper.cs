namespace BilibiliLiveRecordDownLoader.Application.Settings;

internal static class SettingsMapper
{
	extension(AppConfig config)
	{
		public AppSettingsDto ToSettingsDto()
		{
			return new AppSettingsDto
			{
				Language = config.Language,
				Theme = config.Theme,
				AutoCheckUpdate = config.AutoCheckUpdate,
				IncludePreRelease = config.IncludePreRelease,
				WindowWidth = config.WindowWidth,
				WindowHeight = config.WindowHeight,
				RoomSortOrder = config.RoomSortOrder,
				RecordDirectory = config.RecordDirectory,
				FFmpegPath = config.FFmpegPath,
				UserAgent = config.UserAgent,
				AutoRenewCookie = config.AutoRenewCookie,
				UseSystemProxy = config.UseSystemProxy,
				RecorderType = config.RecorderType,
				CodecPriority = config.CodecPriority,
				FormatPriority = config.FormatPriority,
				StreamHostSelection = config.StreamHostSelection,
				ConvertToMp4 = config.ConvertToMp4,
				DeleteAfterConvert = config.DeleteAfterConvert,
				SplitMode = config.SplitMode,
				SplitDuration = config.SplitDuration,
				SplitSizeMebibytes = config.SplitSizeMebibytes,
				SplitOnTitleChange = config.SplitOnTitleChange,
				IncludeTitleInFileName = config.IncludeTitleInFileName,
				NotifyOnRecordingError = config.NotifyOnRecordingError,
				NotifyOnTitleChange = config.NotifyOnTitleChange,
				RemoveFillerData = config.RemoveFillerData,
				RecordSuperChats = config.RecordSuperChats,
				GiftMode = config.GiftMode,
				RecordGuards = config.RecordGuards,
				DanmakuOffset = config.DanmakuOffset,
				GenerateDanmakuSubtitle = config.GenerateDanmakuSubtitle,
				ShowPaidMessagesInSubtitle = config.ShowPaidMessagesInSubtitle,
				SaveRawDanmaku = config.SaveRawDanmaku,
				WriteChaptersOnConvert = config.WriteChaptersOnConvert
			};
		}

		// 编译器只检查 ToSettingsDto 有没有漏掉属性，这个方向由往返测试检查
		public void Apply(AppSettingsDto settings)
		{
			config.Language = settings.Language;
			config.Theme = settings.Theme;
			config.AutoCheckUpdate = settings.AutoCheckUpdate;
			config.IncludePreRelease = settings.IncludePreRelease;
			config.WindowWidth = settings.WindowWidth;
			config.WindowHeight = settings.WindowHeight;
			config.RoomSortOrder = settings.RoomSortOrder;
			config.RecordDirectory = settings.RecordDirectory;
			config.FFmpegPath = settings.FFmpegPath;
			config.UserAgent = settings.UserAgent;
			config.AutoRenewCookie = settings.AutoRenewCookie;
			config.UseSystemProxy = settings.UseSystemProxy;
			config.RecorderType = settings.RecorderType;
			config.CodecPriority = settings.CodecPriority;
			config.FormatPriority = settings.FormatPriority;
			config.StreamHostSelection = settings.StreamHostSelection;
			config.ConvertToMp4 = settings.ConvertToMp4;
			config.DeleteAfterConvert = settings.DeleteAfterConvert;
			config.SplitMode = settings.SplitMode;
			config.SplitDuration = settings.SplitDuration;
			config.SplitSizeMebibytes = settings.SplitSizeMebibytes;
			config.SplitOnTitleChange = settings.SplitOnTitleChange;
			config.IncludeTitleInFileName = settings.IncludeTitleInFileName;
			config.NotifyOnRecordingError = settings.NotifyOnRecordingError;
			config.NotifyOnTitleChange = settings.NotifyOnTitleChange;
			config.RemoveFillerData = settings.RemoveFillerData;
			config.RecordSuperChats = settings.RecordSuperChats;
			config.GiftMode = settings.GiftMode;
			config.RecordGuards = settings.RecordGuards;
			config.DanmakuOffset = settings.DanmakuOffset;
			config.GenerateDanmakuSubtitle = settings.GenerateDanmakuSubtitle;
			config.ShowPaidMessagesInSubtitle = settings.ShowPaidMessagesInSubtitle;
			config.SaveRawDanmaku = settings.SaveRawDanmaku;
			config.WriteChaptersOnConvert = settings.WriteChaptersOnConvert;
		}
	}
}
