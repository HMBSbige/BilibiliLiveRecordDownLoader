using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SplitterSelectionTest
{
	[Test]
	[Arguments(RecorderType.FFmpeg, StreamProtocol.HttpStream, StreamFormat.Flv, "TransportStreamSplitter")]
	[Arguments(RecorderType.FFmpeg, StreamProtocol.HttpHls, StreamFormat.Fmp4, "TransportStreamSplitter")]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpStream, StreamFormat.Flv, "FlvSplitter")]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpHls, StreamFormat.Fmp4, "Fmp4HlsSplitter")]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpHls, StreamFormat.Ts, "TsHlsSplitter")]
	public async Task EachRecordingIsParsedAsTheContainerWrittenToDiskAsync(RecorderType recorderType, StreamProtocol protocol, StreamFormat format, string expected)
	{
		LiveStreamCandidate.Listed candidate = new(protocol, format, StreamCodec.Avc, 10000, [], [new Uri("https://cdn.test/live")]);

		await Assert.That(LiveRecorder.CreateSplitter(candidate, recorderType)?.GetType().Name).IsEqualTo(expected);
	}
}
