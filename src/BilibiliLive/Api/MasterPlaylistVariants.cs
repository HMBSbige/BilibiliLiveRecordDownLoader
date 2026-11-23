using BilibiliLive.Streams;
using System.Globalization;

namespace BilibiliLive.Api;

internal static class MasterPlaylistVariants
{
	private const string Header = "#EXTM3U";
	private const string StreamInfTag = "#EXT-X-STREAM-INF:";

	/// <summary>每种画质和编码在每条 pathway（同一路流的另一个 CDN 主机）下各列一次，合并成一个变体</summary>
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

	// 有的条目把视频编码写两遍而不写音频编码，其实初始化分片里音视频都有，所以只看第一个编码
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
