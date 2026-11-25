using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Collections;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SettingsAppServiceTest
{
	[Test]
	public async Task EverySettingSurvivesTheRoundTripWithoutTouchingCookieAndRoomsAsync(CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.Cookie = "SESSDATA=a";
		config.Rooms = [new RoomConfig { RoomId = 1, IsRecordingEnabled = false }];
		IReadOnlyList<RoomConfig> rooms = config.Rooms;
		AppSettingsDto original = config.ToSettingsDto();
		AppSettingsDto changed = new()
		{
			Language = "en",
			Theme = AppTheme.Dark,
			AutoCheckUpdate = false,
			IncludePreRelease = true,
			WindowWidth = 900,
			WindowHeight = 600,
			RoomSortOrder = RoomSortOrder.Name,
			RecordDirectory = "records",
			FFmpegPath = "ffmpeg",
			UserAgent = "agent",
			AutoRenewCookie = false,
			UseSystemProxy = true,
			RecorderType = RecorderType.FFmpeg,
			CodecPriority = [StreamCodec.Hevc, StreamCodec.Avc, StreamCodec.Av1],
			FormatPriority = [StreamFormat.Flv, StreamFormat.Ts, StreamFormat.Fmp4],
			StreamHostSelection = StreamHostSelection.Random,
			ConvertToMp4 = true,
			DeleteAfterConvert = true,
			SplitMode = RecordingSplitMode.Size,
			SplitDuration = TimeSpan.FromMinutes(30),
			SplitSizeMebibytes = 1024,
			SplitOnTitleChange = true,
			IncludeTitleInFileName = false,
			NotifyOnRecordingError = false,
			NotifyOnTitleChange = true,
			RemoveFillerData = true,
			UseMasterPlaylistWhenRestricted = false,
			RecordSuperChats = false,
			GiftMode = DanmakuGiftMode.Paid,
			RecordGuards = false,
			DanmakuOffset = TimeSpan.FromSeconds(-1.5),
			GenerateDanmakuSubtitle = false,
			ShowPaidMessagesInSubtitle = false,
			SaveRawDanmaku = true,
			WriteChaptersOnConvert = true
		};
		SettingsAppService service = new(new FakeConfigManager(config));

		await service.UpdateAsync(_ => changed, cancellationToken);
		AppSettingsDto saved = await service.GetAsync(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(SameProperties(original, changed)).IsEmpty();
			await Assert.That(saved).IsEquivalentTo(changed);
			await Assert.That(config.Cookie).IsEqualTo("SESSDATA=a");
			await Assert.That(config.Rooms).IsEquivalentTo(rooms);
		}
	}

	private static string[] SameProperties(AppSettingsDto x, AppSettingsDto y)
	{
		return [.. typeof(AppSettingsDto).GetProperties().Where(property => IsSame(property.GetValue(x), property.GetValue(y))).Select(static property => property.Name)];

		static bool IsSame(object? x, object? y)
		{
			return x is IEnumerable first && y is IEnumerable second ? first.Cast<object>().SequenceEqual(second.Cast<object>()) : Equals(x, y);
		}
	}
}
