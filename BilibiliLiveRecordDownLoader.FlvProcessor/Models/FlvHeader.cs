using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
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
        public const string Signature = @"FLV";

        /// <summary>
        /// Only 0x01 is valid
        /// </summary>
        public const byte Version = 0x01;

        /// <summary>
        /// Bitmask: 0x04 is audio, 0x01 is video (so 0x05 is audio+video)
        /// </summary>
        public HeaderFlags Flags = HeaderFlags.VideoAndAudio;

        /// <summary>
        /// Used to skip a newer expanded header
        /// uint32_be
        /// </summary>
        public const uint HeaderSize = 9;

        #endregion

        public int Size => 9;

        public Memory<byte> ToMemory(Memory<byte> array)
        {
            var res = array.Slice(0, Size);

            Encoding.UTF8.GetBytes(Signature, res.Span.Slice(0, 3));
            res.Span[3] = Version;
            res.Span[4] = (byte)Flags;
            BinaryPrimitives.WriteUInt32BigEndian(res.Span.Slice(5, 4), HeaderSize);

            return res;
        }
    }
}
