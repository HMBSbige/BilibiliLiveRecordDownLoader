namespace BilibiliLive.Streams;

/// <summary>Guesses where the untranscoded original of a room is served.</summary>
/// <remarks>
/// A transcoded stream is named live_A_B_C_suffix (suffixes such as 2500, minihevc or proav1), the original live_A_B_C.
/// Some CDN nodes serve the original to anyone who has a signed URL for a transcoded fMP4 stream; FLV and TS are always refused.
/// </remarks>
public static class SourceStreams
{
	private const int TranscodedNameParts = 5;
	private const string StreamNamePrefix = "live";

	// A Blu-ray stream without its suffix is not served, and its room usually offers the original over HEVC anyway
	private const string BluRaySuffix = "bluray";

	/// <summary>Derives one source candidate from every fMP4 candidate whose stream name has a transcoding suffix.</summary>
	/// <remarks>Candidates derived from AVC come first, because those were seen to work; streams of different codecs share one original, so each path is kept once.</remarks>
	public static IReadOnlyList<LiveStreamCandidate.Source> Derive(IEnumerable<LiveStreamCandidate.Listed> candidates)
	{
		ArgumentNullException.ThrowIfNull(candidates);

		HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
		List<LiveStreamCandidate.Source> sources = [];

		foreach (LiveStreamCandidate.Listed candidate in candidates.Where(static candidate => candidate is { Protocol: StreamProtocol.HttpHls, Format: StreamFormat.Fmp4 }).OrderBy(static candidate => candidate.Codec is StreamCodec.Avc ? 0 : 1))
		{
			Uri[] urls = [.. candidate.Urls.Select(TryGetSourceUri).OfType<Uri>().Where(uri => paths.Add(uri.GetLeftPart(UriPartial.Path)))];

			if (urls.Length is not 0)
			{
				sources.Add(new LiveStreamCandidate.Source(candidate.Protocol, candidate.Format, urls));
			}
		}

		return sources;
	}

	/// <summary>Takes the transcoding suffix off the stream name in the path and keeps the query, which carries the signature.</summary>
	/// <returns><see langword="null"/> when no path segment, or more than one, is a transcoded stream name.</returns>
	public static Uri? TryGetSourceUri(Uri uri)
	{
		ArgumentNullException.ThrowIfNull(uri);

		if (!uri.IsAbsoluteUri)
		{
			return null;
		}

		string[] segments = uri.AbsolutePath.Split('/');
		int found = -1;

		for (int index = 0; index < segments.Length; ++index)
		{
			if (TryRemoveSuffix(segments[index]) is not { } segment)
			{
				continue;
			}

			// Which of them names the stream would be a guess
			if (found >= 0)
			{
				return null;
			}

			found = index;
			segments[index] = segment;
		}

		return found < 0 ? null : new Uri(uri, string.Join('/', segments) + uri.Query);
	}

	// The segment may carry an extension, as in live_A_B_C_2500.m3u8
	private static string? TryRemoveSuffix(string segment)
	{
		int extension = segment.IndexOf('.', StringComparison.Ordinal);
		string name = extension < 0 ? segment : segment.Substring(0, extension);
		string[] parts = name.Split('_');

		if (parts is not [StreamNamePrefix, .., var suffix] || parts.Length is not TranscodedNameParts || parts.Any(static part => part.Length is 0) || suffix.Equals(BluRaySuffix, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return string.Join('_', parts, 0, TranscodedNameParts - 1) + (extension < 0 ? string.Empty : segment.Substring(extension));
	}
}
