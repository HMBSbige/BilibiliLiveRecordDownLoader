using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using System;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders
{
    public class FlvTagPayloadInfo : IBytesStruct
    {
        #region Field

        /// <summary>
        /// For first packet set to AMF Metadata
        /// </summary>
        public PacketType PacketType = PacketType.AMF_Metadata;

        /// <summary>
        /// Size of packet data only
        /// uint24_be
        /// </summary>
        public uint PayloadSize;

        #endregion

        public int Size => 4;

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            BinaryPrimitives.WriteUInt32BigEndian(res.Span, PayloadSize);
            res.Span[0] = (byte)PacketType;

            return res;
        }

        public void Read(Memory<byte> buffer)
        {
            throw new NotImplementedException();
        }
    }
}
