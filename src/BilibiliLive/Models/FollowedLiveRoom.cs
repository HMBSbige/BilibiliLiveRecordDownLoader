namespace BilibiliLive.Models;

public sealed record FollowedLiveRoom(long RoomId, string UserName, string Title, LiveStatus LiveStatus);
