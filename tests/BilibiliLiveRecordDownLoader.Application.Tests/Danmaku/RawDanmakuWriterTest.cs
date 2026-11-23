using BilibiliLiveRecordDownLoader.Application.Danmaku;

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
			await writer.WriteAsync(DateTimeOffset.UnixEpoch, "X", "{\"cmd\":\"X\",\r\n\"data\":\n[1, \"a\\nb\", \"弹幕\"]}"u8, cancellationToken);
			await writer.WriteAsync(DateTimeOffset.UnixEpoch, "Y", """{"cmd":"Y", "data":1}"""u8, cancellationToken);
		}

		await Assert.That(await File.ReadAllLinesAsync(path, cancellationToken)).IsEquivalentTo(["""{"t":0,"cmd":"X","raw":{"cmd":"X","data":[1,"a\nb","弹幕"]}}""", """{"t":0,"cmd":"Y","raw":{"cmd":"Y", "data":1}}"""], CollectionOrdering.Matching);
	}

	[Test]
	public async Task ConnectionChangesUseTheirOwnCommandsAsync(CancellationToken cancellationToken)
	{
		using TestDirectory directory = new();
		string path = directory.Combine("a.danmaku.jsonl");

		await using (RawDanmakuWriter writer = RawDanmakuWriter.Open(path, DanmakuFlushPolicy.Default, TimeProvider.System))
		{
			await writer.WriteConnectedAsync(DateTimeOffset.UnixEpoch, true, null, cancellationToken);
			await writer.WriteConnectedAsync(DateTimeOffset.UnixEpoch.AddSeconds(1), false, true, cancellationToken);
			await writer.WriteDisconnectedAsync(DateTimeOffset.UnixEpoch.AddSeconds(2), cancellationToken);
		}

		await Assert.That(await File.ReadAllLinesAsync(path, cancellationToken)).IsEquivalentTo(["""{"t":0,"cmd":"@connected","raw":{"anonymous":true}}""", """{"t":1000,"cmd":"@connected","raw":{"anonymous":false,"masked":true}}""", """{"t":2000,"cmd":"@disconnected","raw":null}"""], CollectionOrdering.Matching);
	}
}
