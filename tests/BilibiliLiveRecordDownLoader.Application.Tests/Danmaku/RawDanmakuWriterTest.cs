using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Danmaku;

public sealed class RawDanmakuWriterTest
{
	[Test]
	public async Task EachMessageStaysOnOneLineAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.danmaku.jsonl");

		await using (RawDanmakuWriter writer = RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, TimeProvider.System))
		{
			await writer.WriteAsync(DateTimeOffset.UnixEpoch, "X", Encoding.UTF8.GetBytes("{\"cmd\":\"X\",\r\n\"data\":\n[1, \"a\\nb\"]}"), cancellationToken);
			await writer.WriteAsync(DateTimeOffset.UnixEpoch, "Y", """{"cmd":"Y", "data":1}"""u8.ToArray(), cancellationToken);
		}

		string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);

		await Assert.That(string.Join('\n', lines)).IsEqualTo(string.Join('\n', """{"t":0,"cmd":"X","raw":{"cmd":"X","data":[1,"a\nb"]}}""", """{"t":0,"cmd":"Y","raw":{"cmd":"Y", "data":1}}"""));
	}
}
