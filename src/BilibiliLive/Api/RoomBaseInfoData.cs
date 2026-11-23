namespace BilibiliLive.Api;

internal sealed record RoomBaseInfoData(IReadOnlyDictionary<string, RoomBaseInfo>? ByRoomIds);

internal sealed record RoomBaseInfo(long RoomId, long ShortId, long Uid, string? Uname, string? Title, int LiveStatus, string? AreaName, string? ParentAreaName, string? LiveTime, string? LiveIdStr);
