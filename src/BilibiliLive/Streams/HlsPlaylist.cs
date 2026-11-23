using System.Globalization;

namespace BilibiliLive.Streams;

internal sealed record HlsPlaylist(TimeSpan? TargetDuration, IReadOnlyList<HlsSegment> Segments, bool EndList)
{
	private const string Header = "#EXTM3U";
	private const string TargetDurationTag = "#EXT-X-TARGETDURATION:";
	private const string MediaSequenceTag = "#EXT-X-MEDIA-SEQUENCE:";
	private const string MapTag = "#EXT-X-MAP:";
	private const string DiscontinuitySequenceTag = "#EXT-X-DISCONTINUITY-SEQUENCE:";
	private const string DiscontinuityTag = "#EXT-X-DISCONTINUITY";
	private const string EndListTag = "#EXT-X-ENDLIST";
	private const string StreamInfTag = "#EXT-X-STREAM-INF:";

	// 远大于任何真实直播流的分片时长；再大的值会让轮询间隔和静默上限长达数天，甚至让 TimeSpan 溢出
	private const double MaxTargetDurationSeconds = 3600;

	public static HlsPlaylist Parse(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		bool hasHeader = false;
		TimeSpan? targetDuration = null;
		long? mediaSequence = null;
		string? mapUri = null;
		long discontinuitySequence = 0;
		bool endList = false;
		List<HlsSegment> segments = [];

		foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
		{
			ReadOnlySpan<char> line = rawLine.Trim();

			if (line.IsEmpty)
			{
				continue;
			}

			if (!hasHeader)
			{
				// 播放列表可能以 BOM 开头
				if (line.TrimStart('\uFEFF') is not Header)
				{
					throw new FormatException("The playlist does not start with #EXTM3U.");
				}

				hasHeader = true;
			}
			else if (line[0] is not '#')
			{
				segments.Add(new HlsSegment(line.ToString(), mapUri, discontinuitySequence, mediaSequence + segments.Count));
			}
			else if (line.StartsWith(StreamInfTag, StringComparison.Ordinal))
			{
				throw new FormatException("A master playlist lists variants instead of segments.");
			}
			else if (line.StartsWith(MediaSequenceTag, StringComparison.Ordinal))
			{
				mediaSequence = ParseInteger(line, MediaSequenceTag);
			}
			else if (line.StartsWith(TargetDurationTag, StringComparison.Ordinal))
			{
				targetDuration = double.TryParse(line.Slice(TargetDurationTag.Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds) && seconds is >= 0 and <= MaxTargetDurationSeconds
					? TimeSpan.FromSeconds(seconds)
					: throw new FormatException($"Invalid {TargetDurationTag} tag.");
			}
			else if (line.StartsWith(MapTag, StringComparison.Ordinal))
			{
				ReadOnlySpan<char> uri = GetAttribute(line.Slice(MapTag.Length), "URI");
				mapUri = uri.IsEmpty ? throw new FormatException($"{MapTag} has no URI.") : uri.ToString();
			}
			else if (line.StartsWith(DiscontinuitySequenceTag, StringComparison.Ordinal))
			{
				discontinuitySequence = ParseInteger(line, DiscontinuitySequenceTag);
			}
			else if (line is DiscontinuityTag)
			{
				++discontinuitySequence;
			}
			else if (line is EndListTag)
			{
				endList = true;
			}
		}

		if (!hasHeader)
		{
			throw new FormatException("The playlist is empty.");
		}

		return new HlsPlaylist(targetDuration, segments, endList);
	}

	// 有的 CDN 节点返回主播放列表，指向边缘节点上同一路流的媒体播放列表
	public static string? GetVariant(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		bool isVariant = false;

		foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
		{
			ReadOnlySpan<char> line = rawLine.Trim();

			if (line.IsEmpty)
			{
				continue;
			}

			if (line[0] is not '#')
			{
				return isVariant ? line.ToString() : null;
			}

			isVariant |= line.StartsWith(StreamInfTag, StringComparison.Ordinal);
		}

		return null;
	}

	/// <returns>没有这个属性和值为空对调用方是一回事，都返回空切片</returns>
	internal static ReadOnlySpan<char> GetAttribute(ReadOnlySpan<char> attributes, ReadOnlySpan<char> name)
	{
		while (!attributes.IsEmpty)
		{
			int equals = attributes.IndexOf('=');

			if (equals < 0)
			{
				return [];
			}

			ReadOnlySpan<char> key = attributes.Slice(0, equals).Trim();
			ReadOnlySpan<char> rest = attributes.Slice(equals + 1);
			ReadOnlySpan<char> value;

			if (rest.StartsWith('"'))
			{
				int close = rest.Slice(1).IndexOf('"');

				if (close < 0)
				{
					return [];
				}

				value = rest.Slice(1, close);
				rest = rest.Slice(close + 2);
			}
			else
			{
				int comma = rest.IndexOf(',');
				value = comma < 0 ? rest : rest.Slice(0, comma);
				rest = comma < 0 ? [] : rest.Slice(comma);
			}

			if (key.SequenceEqual(name))
			{
				return value;
			}

			int next = rest.IndexOf(',');
			attributes = next < 0 ? [] : rest.Slice(next + 1);
		}

		return [];
	}

	private static long ParseInteger(ReadOnlySpan<char> line, string tag)
	{
		return long.TryParse(line.Slice(tag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : throw new FormatException($"Invalid {tag} tag.");
	}
}
