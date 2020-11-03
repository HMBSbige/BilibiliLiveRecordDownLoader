using System;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders
{
    public class Timestamp : IBytesStruct
    {
        #region Field

        /// <summary>
        /// For first packet set to NULL
        /// </summary>
        public uint Lower = 0;

        /// <summary>
        /// Extension to create a uint32_be value
        /// </summary>
        public byte Upper = 0;

        #endregion

        public int Size => 4;

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            BinaryPrimitives.WriteUInt32BigEndian(res.Span, Lower);

            res.Span[0] = res.Span[1];
            res.Span[1] = res.Span[2];
            res.Span[2] = res.Span[3];
            res.Span[3] = Upper;

            return res;
        }
    }
}
