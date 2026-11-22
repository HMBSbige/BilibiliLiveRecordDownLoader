namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class LiveStreamCandidateExtensions
{
	extension(LiveStreamCandidate candidate)
	{
		// 日志和错误提示里区分各路流；源流的编码要连上之后从 init 段看
		public string Name => candidate switch
		{
			LiveStreamCandidate.Listed listed => $"{listed.Format}/{listed.Codec}",
			LiveStreamCandidate.Source source => $"{source.Format}/source"
		};
	}
}
