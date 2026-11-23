using BilibiliLive.Streams;
using System.Globalization;

namespace BilibiliLive.Api;

internal static class MasterPlaylistVariants
{
	private const string Header = "#EXTM3U";
	private const string StreamInfTag = "#EXT-X-STREAM-INF:";

	/// <summary>Every quality and codec is listed once per pathway, another CDN host serving the same stream, so their entries merge into one variant.</summary>
	/// <exception cref="FormatException">The content is not an HLS playlist.</exception>
	public static LiveStreamCandidate.Variant[] Parse(string content, Uri playlistUri)
	{
		bool hasHeader = false;
		(StreamCodec Codec, long Qn)? pending = null;
		List<((StreamCodec Codec, long Qn) Key, Uri Url)> entries = [];

		foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
		{
			ReadOnlySpan<char> line = rawLine.Trim();

			if (line.IsEmpty)
			{
				continue;
			}

			if (!hasHeader)
			{
				if (line.TrimStart('\uFEFF') is not Header)
				{
					throw new FormatException("The response is not an HLS playlist.");
				}

				hasHeader = true;
			}
			else if (line.StartsWith(StreamInfTag, StringComparison.Ordinal))
			{
				pending = ParseStreamInf(line.Slice(StreamInfTag.Length));
			}
			else if (line[0] is not '#')
			{
				if (pending is { } key && Uri.TryCreate(playlistUri, line.ToString(), out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps && !entries.Contains((key, uri)))
				{
					entries.Add((key, uri));
				}

				pending = null;
			}
		}

		if (!hasHeader)
		{
			throw new FormatException("The response is empty.");
		}

		return [.. entries.GroupBy(static entry => entry.Key, static entry => entry.Url).Select(static variant => new LiveStreamCandidate.Variant(variant.Key.Codec, variant.Key.Qn, [.. variant]))];
	}

	private static (StreamCodec Codec, long Qn)? ParseStreamInf(ReadOnlySpan<char> attributes)
	{
		return long.TryParse(HlsPlaylist.GetAttribute(attributes, "BILI-QN"), NumberStyles.None, CultureInfo.InvariantCulture, out long qn) && ParseVideoCodec(HlsPlaylist.GetAttribute(attributes, "CODECS")) is { } codec ? (codec, qn) : null;
	}

	// Some entries name the video codec twice and no audio codec, although their init segments carry both tracks, so only the first codec counts
	private static StreamCodec? ParseVideoCodec(ReadOnlySpan<char> codecs)
	{
		int comma = codecs.IndexOf(',');
		ReadOnlySpan<char> first = (comma < 0 ? codecs : codecs.Slice(0, comma)).Trim();
		int dot = first.IndexOf('.');

		return (dot < 0 ? first : first.Slice(0, dot)) switch
		{
			"avc1" or "avc3" => StreamCodec.Avc,
			"hvc1" or "hev1" => StreamCodec.Hevc,
			"av01" => StreamCodec.Av1,
			_ => null
		};
	}
}
