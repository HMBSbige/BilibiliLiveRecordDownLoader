namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public sealed record ActiveRecordingDto(StreamFormat Format, StreamCodec Codec, long Quality, QualityRestriction? Restriction, DateTimeOffset StartedAt);
