namespace BilibiliLive.Models;

public sealed record FollowedLiveRoom(long RoomId, long UserId, string UserName, string Title, LiveStatus LiveStatus);
