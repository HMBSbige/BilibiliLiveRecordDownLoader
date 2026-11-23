namespace BilibiliLiveRecordDownLoader.Domain.Recording;

public static class StreamPriority
{
	public static IReadOnlyList<StreamCodec> DefaultCodecs { get; } = [StreamCodec.Avc, StreamCodec.Hevc, StreamCodec.Av1];

	public static IReadOnlyList<StreamFormat> DefaultFormats { get; } = [StreamFormat.Fmp4, StreamFormat.Ts, StreamFormat.Flv];

	public static IReadOnlyList<T> Normalize<T>(IEnumerable<T>? priority, IReadOnlyList<T> fallback) where T : struct, Enum
	{
		IReadOnlyList<T> result = [.. (priority ?? []).Where(Enum.IsDefined).Distinct()];
		return result.Count > 0 ? result : fallback;
	}
}
