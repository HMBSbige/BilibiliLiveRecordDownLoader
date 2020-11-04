using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
    public interface IBytesStruct
    {
        /// <summary>
        /// 该结构的大小
        /// </summary>
        int Size { get; }

        Memory<byte> ToMemory(Memory<byte> array);

        public void Read(Memory<byte> buffer) { }
        //ReadOnlySequence<byte> Read(in ReadOnlySequence<byte> sequence);
    }
}
