namespace BilibiliLive.Enums;

public enum StreamEndReason
{
	EndOfStream,

	/// <summary>The stream continues with a new timeline or init segment, which belongs in a new file.</summary>
	Discontinuity
}
