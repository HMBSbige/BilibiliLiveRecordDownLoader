using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders
{
    public class FlvTagHeader : IBytesStruct
    {
        #region Field

        /// <summary>
        /// For first packet set to NULL
        /// uint32_be
        /// </summary>
        public uint SizeofPreviousPacket;

        public FlvTagPayloadInfo PayloadInfo = new FlvTagPayloadInfo();

        /// <summary>
        /// 单位微秒
        /// </summary>
        public FlvTimestamp Timestamp = new FlvTimestamp();

        /// <summary>
        /// For first stream of same type set to NULL
        /// </summary>
        public StreamId StreamId = new StreamId();

        #endregion

        public int Size => 15;

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            BinaryPrimitives.WriteUInt32BigEndian(res.Span, SizeofPreviousPacket);

            PayloadInfo.ToMemory(res.Slice(4));
            Timestamp.ToMemory(res.Slice(8));
            StreamId.ToMemory(res.Slice(12));

            return res;
        }

        public void Read(Span<byte> buffer)
        {
            SizeofPreviousPacket = BinaryPrimitives.ReadUInt32BigEndian(buffer);
            PayloadInfo.Read(buffer.Slice(4));
            Timestamp.Read(buffer.Slice(8));
        }
    }
}
