namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public sealed record CutInput(string Source, string Destination, TimeSpan Start, TimeSpan End);
