using System.Net.Http.Headers;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class FileRange
    {
        public RangeHeaderValue Range { get; set; }
        public string FileName { get; set; }
    }
}
