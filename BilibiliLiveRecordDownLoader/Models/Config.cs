namespace BilibiliLiveRecordDownLoader.Models
{
    public class Config
    {
        public long RoomId { get; set; }
        public string MainDir { get; set; }
        public byte DownloadThreads { get; set; }
    }
}
