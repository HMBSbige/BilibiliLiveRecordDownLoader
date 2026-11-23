namespace BilibiliLiveRecordDownLoader.Domain.Recording;

// 录制中途也会改，每到一个切分点按当时的设置重新算
public sealed record RecordingSplitPolicy(RecordingSplit Split, bool OnTitleChange)
{
	public static RecordingSplitPolicy Create(AppConfig app, RoomConfig room)
	{
		return new RecordingSplitPolicy(CreateSplit(app, room), room.SplitOnTitleChange ?? app.SplitOnTitleChange);
	}

	// 房间只覆盖了模式时数值取全局的；跟随全局时房间残留的数值不算数
	private static RecordingSplit CreateSplit(AppConfig app, RoomConfig room)
	{
		return room.SplitMode is { } mode
			? RecordingSplit.Create(mode, room.SplitDuration ?? app.SplitDuration, room.SplitSizeMebibytes ?? app.SplitSizeMebibytes)
			: RecordingSplit.Create(app.SplitMode, app.SplitDuration, app.SplitSizeMebibytes);
	}
}
