namespace BilibiliLive.Models;

public sealed record LiveRoomInfo(long RoomId, long ShortId, long UserId, string UserName, string Title, LiveStatus LiveStatus);
