using System.Text.Json.Serialization;

namespace BilibiliLive.Api;

internal sealed record FollowingData([property: JsonPropertyName("totalPage")] int TotalPage, List<FollowingRoom>? List);

internal sealed record FollowingRoom([property: JsonPropertyName("roomid")] long RoomId, string? Uname, string? Title, int LiveStatus);
