using System;
using System.IO;

namespace BilibiliLiveRecordDownLoader.Http
{
    public class FileChunk
    {
        public int Start { get; set; }
        public int End { get; set; }
        public string TempFileName { get; }

        public FileChunk(int startByte, int endByte, string tempPath)
        {
            TempFileName = Path.Combine(tempPath, Guid.NewGuid().ToString());
            using var _ = File.Create(TempFileName);
            Start = startByte;
            End = endByte;
        }
    }
}
