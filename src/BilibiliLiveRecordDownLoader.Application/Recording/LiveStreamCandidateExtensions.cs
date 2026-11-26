namespace BilibiliLiveRecordDownLoader.Application.Recording;

internal static class LiveStreamCandidateExtensions
{
	extension(LiveStreamCandidate candidate)
	{
		public string Name => candidate switch
		{
			LiveStreamCandidate.Listed listed => $"{listed.Format}/{listed.Codec}",
			LiveStreamCandidate.Variant variant => string.Create(CultureInfo.InvariantCulture, $"{variant.Format}/{variant.Codec} {variant.Qn} from the master playlist")
		};

		public string DisplayName => candidate switch
		{
			LiveStreamCandidate.Listed => candidate.Name,
			LiveStreamCandidate.Variant variant => AppLanguage.Current.RecordingVariantStream.ToString(variant.Format, variant.Codec, new StreamQuality(variant.Qn).GetDisplayName(variant.QualityName))
		};

		public string Source => candidate switch
		{
			LiveStreamCandidate.Listed => "getRoomPlayInfo",
			LiveStreamCandidate.Variant => "the master playlist"
		};
	}
}
