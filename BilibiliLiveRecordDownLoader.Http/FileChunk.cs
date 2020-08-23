using System;
using System.IO;

namespace BilibiliLiveRecordDownLoader.Http
{
    public class FileChunk
    {
        public long Start { get; set; }
        public long End { get; set; }
        public string TempFileName { get; }

        public FileChunk(long startByte, long endByte, string tempPath)
        {
            TempFileName = Path.Combine(tempPath, Guid.NewGuid().ToString());
            using var _ = File.Create(TempFileName);
            Start = startByte;
            End = endByte;
        }
    }
}
