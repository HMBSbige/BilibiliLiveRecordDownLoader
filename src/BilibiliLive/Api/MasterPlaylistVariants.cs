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
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(playlistUri);

		bool hasHeader = false;
		(StreamCodec Codec, long Qn)? pending = null;
		List<(StreamCodec Codec, long Qn, List<Uri> Urls)> variants = [];

		foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
		{
			ReadOnlySpan<char> line = rawLine.Trim();

			if (line.IsEmpty)
			{
				continue;
			}

			if (!hasHeader)
			{
				if (!line.TrimStart('\uFEFF').SequenceEqual(Header))
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
				if (pending is { } key && Uri.TryCreate(playlistUri, line.ToString(), out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
				{
					Add(variants, key, uri);
				}

				pending = null;
			}
		}

		if (!hasHeader)
		{
			throw new FormatException("The response is empty.");
		}

		return [.. variants.Select(static variant => new LiveStreamCandidate.Variant(variant.Codec, variant.Qn, variant.Urls))];
	}

	private static void Add(List<(StreamCodec Codec, long Qn, List<Uri> Urls)> variants, (StreamCodec Codec, long Qn) key, Uri uri)
	{
		foreach ((StreamCodec codec, long qn, List<Uri> urls) in variants)
		{
			if (codec == key.Codec && qn == key.Qn)
			{
				if (!urls.Contains(uri))
				{
					urls.Add(uri);
				}

				return;
			}
		}

		variants.Add((key.Codec, key.Qn, [uri]));
	}

	private static (StreamCodec Codec, long Qn)? ParseStreamInf(ReadOnlySpan<char> attributes)
	{
		if (!long.TryParse(HlsPlaylist.GetAttribute(attributes, "BILI-QN"), NumberStyles.None, CultureInfo.InvariantCulture, out long qn) || ParseVideoCodec(HlsPlaylist.GetAttribute(attributes, "CODECS")) is not { } codec)
		{
			return null;
		}

		return (codec, qn);
	}

	// Some entries name the video codec twice and no audio codec, although their init segments carry both tracks, so only the first codec counts
	private static StreamCodec? ParseVideoCodec(string? codecs)
	{
		if (codecs is null)
		{
			return null;
		}

		ReadOnlySpan<char> first = codecs.AsSpan();
		int comma = first.IndexOf(',');
		first = (comma < 0 ? first : first.Slice(0, comma)).Trim();
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
