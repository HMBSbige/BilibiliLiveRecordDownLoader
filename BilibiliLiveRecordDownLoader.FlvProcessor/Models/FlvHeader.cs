using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;
using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models
{
    public class FlvHeader : IBytesStruct
    {
        #region Field

        /// <summary>
        /// Always "FLV"
        /// </summary>
        public string Signature = @"FLV";

        /// <summary>
        /// Only 0x01 is valid
        /// </summary>
        public byte Version = 0x01;

        /// <summary>
        /// Bitmask: 0x04 is audio, 0x01 is video (so 0x05 is audio+video)
        /// </summary>
        public HeaderFlags Flags = HeaderFlags.VideoAndAudio;

        /// <summary>
        /// Used to skip a newer expanded header
        /// uint32_be
        /// </summary>
        public uint HeaderSize = 9;

        public uint Reserved => 0;

        #endregion

        public int Size => 13;

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            Encoding.UTF8.GetBytes(Signature, res.Span.Slice(0, 3));
            res.Span[3] = Version;
            res.Span[4] = (byte)Flags;
            BinaryPrimitives.WriteUInt32BigEndian(res.Span.Slice(5, 4), HeaderSize);
            BinaryPrimitives.WriteUInt32BigEndian(res.Span.Slice(9, 4), Reserved);

            return res;
        }

        public void Read(Span<byte> buffer)
        {
            Signature = Encoding.UTF8.GetString(buffer.Slice(0, 3));
            Version = buffer[3];
            Flags = (HeaderFlags)buffer[4];
            HeaderSize = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(5, 4));
        }
    }
}
