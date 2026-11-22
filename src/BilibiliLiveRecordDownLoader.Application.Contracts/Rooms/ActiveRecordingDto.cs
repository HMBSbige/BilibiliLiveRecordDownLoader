namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// 去掉转码后缀的源流只能从 init 段认出编码，认不出时 Codec 为 null；画质不受限时 Restriction 为 null
public sealed record ActiveRecordingDto(StreamFormat Format, StreamCodec? Codec, long Quality, QualityRestriction? Restriction, DateTimeOffset StartedAt);
