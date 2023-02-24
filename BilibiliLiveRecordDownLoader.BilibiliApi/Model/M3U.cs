using System.Text;

namespace BilibiliApi.Model;

public readonly struct M3U
{
	public const string FileHeader = @"#EXTM3U";
	public const string VersionDirective = @"#EXT-X-VERSION:";
	public const string TrackInfoDirective = @"#EXTINF:";
	public const string EndOfListSignalDirective = @"#EXT-X-ENDLIST";

	public int Version { get; }

	public IReadOnlyList<string> Segments { get; }

	public M3U(Stream stream)
	{
		using StreamReader reader = new(stream, Encoding.UTF8);
		if (reader.ReadLine() is not FileHeader)
		{
			throw new FormatException(@"NOT M3U");
		}

		List<string> list = new();

		while (reader.ReadLine() is { } line)
		{
			if (line.StartsWith(TrackInfoDirective))
			{
				string? file = reader.ReadLine();
				if (file is null)
				{
					break;
				}
				list.Add(file);
			}
			else if (line.StartsWith(VersionDirective) && int.TryParse(line[VersionDirective.Length..], out int version))
			{
				Version = version;
			}
			else if (line.StartsWith(EndOfListSignalDirective))
			{
				break;
			}
		}

		Segments = list;
	}
}