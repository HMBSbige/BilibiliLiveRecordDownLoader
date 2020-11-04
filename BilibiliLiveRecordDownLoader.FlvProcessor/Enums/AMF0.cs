namespace BilibiliLiveRecordDownLoader.FlvProcessor.Enums
{
    /// <summary>
    /// https://en.wikipedia.org/wiki/Action_Message_Format#AMF0
    /// </summary>
    public enum AMF0 : byte
    {
        Number = 0x00,
        Boolean = 0x01,
        String = 0x02,
        Object = 0x03,
        Null = 0x05,
        ECMAArray = 0x08,
        ObjectEnd = 0x09,
        StrictArray = 0x0a,
        Date = 0x0b,
        LongString = 0x0c,
        XMLDocument = 0x0f,
        TypedObject = 0x10,
        SwitchToAMF3 = 0x11,
    }
}
