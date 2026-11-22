using System.Globalization;
using System.Text;

namespace MediaToolkit.FFmpeg;

// FFmpeg 的 ffmetadata 格式，给 -map_chapters 用的章节输入
public static class FFmpegMetadata
{
	// BOM 会让 FFmpeg 认不出开头的 ;FFMETADATA1
	private static readonly UTF8Encoding Utf8 = new(false);

	public static async Task WriteChaptersFileAsync(string path, IReadOnlyList<FFmpegChapter> chapters, CancellationToken cancellationToken)
	{
		await File.WriteAllTextAsync(path, FormatChapters(chapters), Utf8, cancellationToken);
	}

	// TIMEBASE、START、END 必须按这个顺序各占一行，FFmpeg 是逐行按顺序读的
	public static string FormatChapters(IReadOnlyList<FFmpegChapter> chapters)
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

	// = ; # \ 和换行分别是键值分隔符、注释、转义和行尾，前面加反斜杠才按字面读；回车先并成换行，单独的回车也会被当成行尾
	public static string Escape(string value)
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

		return builder.ToString();
	}
}
