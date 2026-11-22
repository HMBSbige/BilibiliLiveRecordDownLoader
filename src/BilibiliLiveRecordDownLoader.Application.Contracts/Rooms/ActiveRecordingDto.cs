namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// 画质不受限时 Restriction 为 null
public sealed record ActiveRecordingDto(StreamFormat Format, StreamCodec Codec, long Quality, QualityRestriction? Restriction, DateTimeOffset StartedAt);
