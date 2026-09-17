namespace BilibiliLive.Api;

internal sealed record RoomBaseInfoData(Dictionary<string, RoomBaseInfo>? ByRoomIds);

internal sealed record RoomBaseInfo(long RoomId, long ShortId, long Uid, string? Uname, string? Title, int LiveStatus);
