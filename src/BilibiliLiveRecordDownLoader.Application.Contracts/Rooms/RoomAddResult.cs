namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public closed record RoomAddResult(long RequestedRoomId)
{
	public sealed record Added(long RequestedRoomId, RoomSnapshot Room) : RoomAddResult(RequestedRoomId);

	public sealed record Skipped(long RequestedRoomId, long RoomId) : RoomAddResult(RequestedRoomId);

	public sealed record Failed(long RequestedRoomId, string Error) : RoomAddResult(RequestedRoomId);
}
