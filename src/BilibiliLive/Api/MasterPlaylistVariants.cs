using BilibiliLive.Streams;
using System.Globalization;

namespace BilibiliLive.Api;

internal static class MasterPlaylistVariants
{
	/// <summary>每种画质和编码在每条 pathway（同一路流的另一个 CDN 主机）下各列一次，合并成一个变体</summary>
	public static LiveStreamCandidate.Variant[] Parse(string content, Uri playlistUri)
	{
		bool hasHeader = false;
		(StreamCodec Codec, long Qn, string? Name)? pending = null;
		OrderedDictionary<(StreamCodec Codec, long Qn), (string? Name, List<Uri> Urls)> variants = [];

		foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
		{
			ReadOnlySpan<char> line = rawLine.Trim();

			if (line.IsEmpty)
			{
				continue;
			}

			if (!hasHeader)
			{
				if (!HlsPlaylist.IsHeader(line))
				{
					throw new FormatException("The response is not an HLS playlist.");
				}

				hasHeader = true;
			}
			else if (line.StartsWith(HlsPlaylist.StreamInfTag, StringComparison.Ordinal))
			{
				pending = ParseStreamInf(line.Slice(HlsPlaylist.StreamInfTag.Length));
			}
			else if (line[0] is not '#')
			{
				if (pending is var (codec, qn, name) && Uri.TryCreate(playlistUri, line.ToString(), out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
				{
					Add(variants, (codec, qn), name, uri);
				}

				pending = null;
			}
		}

		if (!hasHeader)
		{
			throw new FormatException("The response is empty.");
		}

		return [.. variants.Select(static variant => new LiveStreamCandidate.Variant(variant.Key.Codec, variant.Key.Qn, variant.Value.Name, variant.Value.Urls))];
	}

	// 同一个地址只留一次，名称取第一个给出的
	private static void Add(OrderedDictionary<(StreamCodec Codec, long Qn), (string? Name, List<Uri> Urls)> variants, (StreamCodec Codec, long Qn) key, string? name, Uri uri)
	{
		if (!variants.TryGetValue(key, out (string? Name, List<Uri> Urls) variant, out int index))
		{
			variants.Add(key, (name, [uri]));
			return;
		}

		if (!variant.Urls.Contains(uri))
		{
			variant.Urls.Add(uri);
		}

		if (variant.Name is null)
		{
			variants.SetAt(index, (name, variant.Urls));
		}
	}

	private static (StreamCodec Codec, long Qn, string? Name)? ParseStreamInf(ReadOnlySpan<char> attributes)
	{
		if (!long.TryParse(HlsPlaylist.GetAttribute(attributes, "BILI-QN"), NumberStyles.None, CultureInfo.InvariantCulture, out long qn) || ParseVideoCodec(HlsPlaylist.GetAttribute(attributes, "CODECS")) is not { } codec)
		{
			return null;
		}

		ReadOnlySpan<char> name = HlsPlaylist.GetAttribute(attributes, "BILI-DISPLAY").Trim();
		return (codec, qn, name.IsEmpty ? null : name.ToString());
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
