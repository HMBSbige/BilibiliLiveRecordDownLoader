namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal sealed record RecordingStream(StreamFormat Format, StreamCodec Codec, StreamQuality Quality, QualityRestriction? Restriction, IReadOnlyDictionary<long, string> QualityNames);
