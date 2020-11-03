namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums
{
    public enum PacketType : byte
    {
        AudioPayload = 0b0000_1000,
        VideoPayload = 0b0000_1001,
        AMF_Metadata = 0b0001_0010,
    }
}
