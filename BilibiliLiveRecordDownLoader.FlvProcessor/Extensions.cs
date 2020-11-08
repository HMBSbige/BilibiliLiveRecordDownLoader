using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;

namespace BilibiliLiveRecordDownLoader.FlvProcessor
{
    public static class Extensions
    {
        public static HeaderFlags ToFlvHeaderFlags(this byte b)
        {
            return (HeaderFlags)b & HeaderFlags.VideoAndAudio;
        }
    }
}
