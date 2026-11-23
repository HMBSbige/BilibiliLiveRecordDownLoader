namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public sealed record RecordingSplitPolicy(RecordingSplit Split, bool OnTitleChange)
{
	public static RecordingSplitPolicy Create(AppConfig app, RoomConfig room)
	{
		return new RecordingSplitPolicy(CreateSplit(app, room), room.SplitOnTitleChange ?? app.SplitOnTitleChange);
	}

	private static RecordingSplit CreateSplit(AppConfig app, RoomConfig room)
	{
		return room.SplitMode is { } mode
			? RecordingSplit.Create(mode, room.SplitDuration ?? app.SplitDuration, room.SplitSizeMebibytes ?? app.SplitSizeMebibytes)
			: RecordingSplit.Create(app.SplitMode, app.SplitDuration, app.SplitSizeMebibytes);
	}
}
