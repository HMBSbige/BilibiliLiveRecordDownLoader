using BilibiliLiveRecordDownLoader.Application.Danmaku;
using System.Text;
using TUnit.Assertions.Enums;

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

		await Assert.That(await File.ReadAllLinesAsync(path, cancellationToken)).IsEquivalentTo(["""{"t":0,"cmd":"X","raw":{"cmd":"X","data":[1,"a\nb"]}}""", """{"t":0,"cmd":"Y","raw":{"cmd":"Y", "data":1}}"""], CollectionOrdering.Matching);
	}

	// 不记连接所用账号的 uid；还不知道是否打码时不写 masked
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
