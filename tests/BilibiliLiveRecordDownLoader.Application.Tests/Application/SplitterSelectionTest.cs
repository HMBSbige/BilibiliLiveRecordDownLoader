using BilibiliLive.Models;
using BilibiliLiveRecordDownLoader.Application.Recording;
using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SplitterSelectionTest
{
	[Test]
	[Arguments(RecorderType.FFmpeg, StreamProtocol.HttpStream, StreamFormat.Flv, typeof(Fmp4Splitter))]
	[Arguments(RecorderType.FFmpeg, StreamProtocol.HttpHls, StreamFormat.Ts, typeof(Fmp4Splitter))]
	[Arguments(RecorderType.FFmpeg, StreamProtocol.HttpHls, StreamFormat.Fmp4, typeof(Fmp4Splitter))]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpStream, StreamFormat.Flv, typeof(FlvSplitter))]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpHls, StreamFormat.Fmp4, typeof(Fmp4HlsSplitter))]
	[Arguments(RecorderType.BuiltIn, StreamProtocol.HttpHls, StreamFormat.Ts, typeof(TsHlsSplitter))]
	public async Task EachRecordingIsParsedAsTheContainerWrittenToDiskAsync(RecorderType recorderType, StreamProtocol protocol, StreamFormat format, Type expected)
	{
		LiveStreamCandidate.Listed candidate = new(protocol, format, StreamCodec.Avc, 10000, [], [new Uri("https://cdn.test/live")]);

		await Assert.That(LiveRecorder.CreateSplitter(candidate, recorderType).GetType()).IsEqualTo(expected);
	}
}
