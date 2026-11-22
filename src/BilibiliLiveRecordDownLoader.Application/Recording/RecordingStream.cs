namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 实际在录的流；源流的编码来自 init 段，认不出时为 null
internal sealed record RecordingStream(StreamFormat Format, StreamCodec? Codec, long Quality, QualityRestriction? Restriction);
