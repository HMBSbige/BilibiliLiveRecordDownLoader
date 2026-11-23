namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public closed record RoomAddResult(long RequestedRoomId)
{
	public sealed record Added(long RequestedRoomId) : RoomAddResult(RequestedRoomId);

	public sealed record Skipped(long RequestedRoomId) : RoomAddResult(RequestedRoomId);

	public sealed record Failed(long RequestedRoomId, string Error) : RoomAddResult(RequestedRoomId);
}
