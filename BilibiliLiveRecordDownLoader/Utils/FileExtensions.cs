using System.Collections.Immutable;
using System.IO;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Utils;

public static class FileExtensions
{
	private static readonly ImmutableHashSet<char> InvalidFileNameChars = ImmutableHashSet.Create(Path.GetInvalidFileNameChars());

	public static string RemoveInvalidFileNameChars(this string path)
	{
		var sb = new StringBuilder(path.Length);
		foreach (var c in path)
		{
			if (!InvalidFileNameChars.Contains(c))
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}
}
