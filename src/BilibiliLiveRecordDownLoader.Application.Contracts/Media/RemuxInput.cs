namespace BilibiliLiveRecordDownLoader.Application.Contracts.Media;

public sealed record RemuxInput(string Source, string Destination, bool DeleteSource, bool RepairFlv);
