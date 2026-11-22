namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record FollowedRoomDto(long RoomId, string UserName, string Title, LiveStatus LiveStatus, bool IsAdded);
