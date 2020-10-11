using System.IO;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class FileChunk
    {
        public long Start { get; set; }
        public long End { get; set; }
        public string TempFileName { get; }

        public FileChunk(long startByte, long endByte, string tempPath)
        {
            TempFileName = Path.Combine(tempPath, Path.GetRandomFileName());
            Start = startByte;
            End = endByte;
        }
    }
}
