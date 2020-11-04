using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagPackets
{
    public class AMFMetadata : IBytesStruct
    {
        public int Size { get; }
        public Memory<byte> ToMemory(Memory<byte> array)
        {
            throw new NotImplementedException();
        }

        public void Read(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }
    }
}
