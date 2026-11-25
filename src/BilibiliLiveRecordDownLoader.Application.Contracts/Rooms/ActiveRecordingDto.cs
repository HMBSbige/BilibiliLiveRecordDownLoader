namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// QualityNames 是接口给这个直播间各画质起的名称，显示 Quality 和 Restriction 里的画质时用
public sealed record ActiveRecordingDto(StreamFormat Format, StreamCodec Codec, long Quality, QualityRestriction? Restriction, IReadOnlyDictionary<long, string> QualityNames, DateTimeOffset StartedAt);
