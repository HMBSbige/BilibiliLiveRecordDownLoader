using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums
{
    [Flags]
    public enum HeaderFlags : byte
    {
        Video = 0b0001,
        Audio = 0b0100,
        VideoAndAudio = 0b0101
    }
}
