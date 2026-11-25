using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SplitterSelectionTest
{
	[Test]
	[Arguments(StreamProtocol.HttpStream, StreamFormat.Flv, typeof(FlvSplitter))]
	[Arguments(StreamProtocol.HttpHls, StreamFormat.Fmp4, typeof(Fmp4HlsSplitter))]
	[Arguments(StreamProtocol.HttpHls, StreamFormat.Ts, typeof(TsHlsSplitter))]
	public async Task EachRecordingIsParsedAsTheContainerWrittenToDiskAsync(StreamProtocol protocol, StreamFormat format, Type expected)
	{
		LiveStreamCandidate.Listed candidate = new(protocol, format, StreamCodec.Avc, 10000, [], [new Uri("https://cdn.test/live")]);

		await Assert.That(LiveRecorder.CreateSplitter(candidate).GetType()).IsEqualTo(expected);
	}
}
