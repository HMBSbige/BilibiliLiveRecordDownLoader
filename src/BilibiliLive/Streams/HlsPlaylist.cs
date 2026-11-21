using System.Globalization;

namespace BilibiliLive.Streams;

public sealed record HlsPlaylist(int Version, TimeSpan? TargetDuration, IReadOnlyList<HlsSegment> Segments, bool EndList)
{
	private const string Header = "#EXTM3U";
	private const string VersionTag = "#EXT-X-VERSION:";
	private const string TargetDurationTag = "#EXT-X-TARGETDURATION:";
	private const string MapTag = "#EXT-X-MAP:";
	private const string DiscontinuitySequenceTag = "#EXT-X-DISCONTINUITY-SEQUENCE:";
	private const string DiscontinuityTag = "#EXT-X-DISCONTINUITY";
	private const string EndListTag = "#EXT-X-ENDLIST";
	private const string StreamInfTag = "#EXT-X-STREAM-INF:";

	public static HlsPlaylist Parse(string content)
	{
		ArgumentNullException.ThrowIfNull(content);

		bool hasHeader = false;
		int version = 1;
		TimeSpan? targetDuration = null;
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
				if (!line.TrimStart('\uFEFF').SequenceEqual(Header))
				{
					throw new FormatException("The playlist does not start with #EXTM3U.");
				}

				hasHeader = true;
			}
			else if (line[0] is not '#')
			{
				segments.Add(new HlsSegment(line.ToString(), mapUri, discontinuitySequence));
			}
			else if (line.StartsWith(StreamInfTag, StringComparison.Ordinal))
			{
				throw new FormatException("A master playlist lists variants instead of segments.");
			}
			else if (line.StartsWith(VersionTag, StringComparison.Ordinal))
			{
				version = int.TryParse(line.Slice(VersionTag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : throw new FormatException($"Invalid {VersionTag} tag.");
			}
			else if (line.StartsWith(TargetDurationTag, StringComparison.Ordinal))
			{
				targetDuration = double.TryParse(line.Slice(TargetDurationTag.Length), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds)
					? TimeSpan.FromSeconds(seconds)
					: throw new FormatException($"Invalid {TargetDurationTag} tag.");
			}
			else if (line.StartsWith(MapTag, StringComparison.Ordinal))
			{
				mapUri = GetQuotedAttribute(line.Slice(MapTag.Length), "URI") ?? throw new FormatException($"{MapTag} has no URI.");
			}
			else if (line.StartsWith(DiscontinuitySequenceTag, StringComparison.Ordinal))
			{
				discontinuitySequence = long.TryParse(line.Slice(DiscontinuitySequenceTag.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
					? value
					: throw new FormatException($"Invalid {DiscontinuitySequenceTag} tag.");
			}
			else if (line.SequenceEqual(DiscontinuityTag))
			{
				++discontinuitySequence;
			}
			else if (line.SequenceEqual(EndListTag))
			{
				endList = true;
			}
		}

		if (!hasHeader)
		{
			throw new FormatException("The playlist is empty.");
		}

		return new HlsPlaylist(version, targetDuration, segments, endList);
	}

	// Some CDN nodes answer with a master playlist that points at the media playlist of the same stream on an edge node
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

	private static string? GetQuotedAttribute(ReadOnlySpan<char> attributes, ReadOnlySpan<char> name)
	{
		while (!attributes.IsEmpty)
		{
			int equals = attributes.IndexOf('=');

			if (equals < 0)
			{
				return null;
			}

			ReadOnlySpan<char> key = attributes.Slice(0, equals).Trim();
			ReadOnlySpan<char> rest = attributes.Slice(equals + 1);
			ReadOnlySpan<char> value;

			if (rest.StartsWith('"'))
			{
				int close = rest.Slice(1).IndexOf('"');

				if (close < 0)
				{
					return null;
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
				return value.ToString();
			}

			int next = rest.IndexOf(',');
			attributes = next < 0 ? [] : rest.Slice(next + 1);
		}

		return null;
	}
}
