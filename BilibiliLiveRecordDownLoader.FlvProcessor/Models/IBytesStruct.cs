using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models
{
    public interface IBytesStruct
    {
        /// <summary>
        /// 该结构的大小
        /// </summary>
        int Size { get; }

        Memory<byte> ToMemory(Memory<byte> array);

        //ReadOnlySequence<byte> Read(in ReadOnlySequence<byte> sequence);
    }
}
