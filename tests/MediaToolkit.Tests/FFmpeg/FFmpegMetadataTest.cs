using MediaToolkit.FFmpeg;
using System.Text;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegMetadataTest
{
	[Test]
	[Arguments("a=b", "a\\=b")]
	[Arguments("a;b", "a\\;b")]
	[Arguments("#a", "\\#a")]
	[Arguments("a\\b", "a\\\\b")]
	[Arguments("a\nb", "a\\\nb")]
	[Arguments("a\r\nb", "a\\\nb")]
	[Arguments("a\rb", "a\\\nb")]
	[Arguments("高能：草", "高能：草")]
	[Arguments("orz\\", "orz\\\\ ")]
	[Arguments("a\\\0", "a\\\\ ")]
	[Arguments("a\\\n", "a\\\\\\\n")]
	public async Task EscapesSpecialCharactersAsync(string value, string expected)
	{
		await Assert.That(FFmpegMetadata.Escape(value)).IsEqualTo(expected);
	}

	[Test]
	public async Task FormatsChaptersInMillisecondsAsync()
	{
		FFmpegChapter[] chapters =
		[
			new(TimeSpan.Zero, TimeSpan.FromMilliseconds(12345.6), @"开场"),
			new(TimeSpan.FromMilliseconds(12345.6), TimeSpan.FromHours(2), @"标题：a=b;c")
		];

		string text = FFmpegMetadata.FormatChapters(chapters);

		await Assert.That(text).IsEqualTo(";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=12345\ntitle=开场\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=12345\nEND=7200000\ntitle=标题：a\\=b\\;c\n");
	}

	[Test]
	public async Task RejectsChaptersThatEndBeforeTheyStartAsync()
	{
		await Assert.That(static () => FFmpegMetadata.FormatChapters([new FFmpegChapter(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), @"x")])).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task FileIsUtf8WithoutByteOrderMarkAsync()
	{
		using TestDirectory directory = new();
		string path = directory.Combine(@"chapters.txt");
		FFmpegChapter[] chapters = [new(TimeSpan.Zero, TimeSpan.FromSeconds(1), @"章节")];

		await FFmpegMetadata.WriteChaptersFileAsync(path, chapters, CancellationToken.None);

		await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(Encoding.UTF8.GetBytes(FFmpegMetadata.FormatChapters(chapters)), CollectionOrdering.Matching);
	}
}
