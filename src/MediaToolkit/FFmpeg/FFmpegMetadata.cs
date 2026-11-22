using System.Globalization;
using System.Text;

namespace MediaToolkit.FFmpeg;

public static class FFmpegMetadata
{
	// 带 BOM 时 FFmpeg 探测不出格式
	private static readonly UTF8Encoding Utf8 = new(false);

	public static async Task WriteChaptersFileAsync(string path, IReadOnlyList<FFmpegChapter> chapters, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(path, FormatChapters(chapters), Utf8, cancellationToken);
	}

	// FFmpeg 按顺序逐行读 TIMEBASE、START、END
	internal static string FormatChapters(IReadOnlyList<FFmpegChapter> chapters)
	{
		StringBuilder builder = new(";FFMETADATA1\n");

		foreach (FFmpegChapter chapter in chapters)
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(chapter.Start, TimeSpan.Zero);
			ArgumentOutOfRangeException.ThrowIfLessThan(chapter.End, chapter.Start);

			builder.Append("[CHAPTER]\nTIMEBASE=1/1000\n");
			builder.Append(CultureInfo.InvariantCulture, $"START={chapter.Start.Ticks / TimeSpan.TicksPerMillisecond}\n");
			builder.Append(CultureInfo.InvariantCulture, $"END={chapter.End.Ticks / TimeSpan.TicksPerMillisecond}\n");
			builder.Append(@"title=").Append(Escape(chapter.Title)).Append('\n');
		}

		return builder.ToString();
	}

	// FFmpeg 读值时把回车也当行尾
	internal static string Escape(string value)
	{
		StringBuilder builder = new(value.Length);
		string normalized = value.ReplaceLineEndings("\n");

		foreach (char c in normalized)
		{
			if (c is '\0')
			{
				continue;
			}

			if (c is '=' or ';' or '#' or '\\' or '\n')
			{
				builder.Append('\\');
			}

			builder.Append(c);
		}

		// FFmpeg 只看换行前一个字节是不是反斜杠，不管它是否已被转义，以 \\ 结尾的值会吞掉行尾
		if (builder.Length > 0 && builder[^1] is '\\')
		{
			builder.Append(' ');
		}

		return builder.ToString();
	}
}
