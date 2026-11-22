using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using System.Web;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class RoomStreamRequestTest
{
	[Test]
	[Timeout(30_000)]
	[Arguments(false, "10000")]
	[Arguments(true, "25000")]
	public async Task StreamCandidatesAreRequestedAtTheTrueOriginalOnlyWhenTurnedOnAsync(bool requestTrueOriginal, string expected, CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.Config.RequestTrueOriginal = requestTrueOriginal;
		loop.GoLive();

		_ = await loop.NextStreamAsync();

		await Assert.That(HttpUtility.ParseQueryString(loop.PlayInfoQuery ?? string.Empty)["qn"]).IsEqualTo(expected);
	}

	[Test]
	[Timeout(30_000)]
	public async Task RecordingLogShowsTheStreamPathWithoutTheQueryAsync(CancellationToken cancellationToken)
	{
		await using RecordingLoopHarness loop = new(cancellationToken);
		loop.GoLive();

		StreamRequest request = await loop.NextStreamAsync();
		await request.AcceptAsync();
		await request.CopyStarted.WaitAsync(cancellationToken);
		string logged = await Assert.That(loop.RecorderLogs.Where(static message => message.StartsWith("Recording ", StringComparison.Ordinal))).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(logged).Contains($"{request.Host}/live/avc.flv ");
			// 假接口给的地址带着 ?sign=…，B 站真实地址的查询参数里还有用户的 uid
			await Assert.That(logged).DoesNotContain("sign=");
		}
	}
}
