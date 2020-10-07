using BilibiliApi.Enums;
using System;
using System.Buffers;
using System.Buffers.Binary;

namespace BilibiliApi.Model.Danmu
{
    public class DanmuPacket
    {
        /// <summary>
        /// 消息总长度，HeaderLength + bytes(Body).Length
        /// </summary>
        public int PacketLength { get; set; }

        /// <summary>
        /// 消息头长度，默认 16
        /// </summary>
        public short HeaderLength { get; set; }

        /// <summary>
        /// 消息版本号
        /// </summary>
        public short ProtocolVersion { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public Operation Operation { get; set; }

        /// <summary>
        /// 参数
        /// </summary>
        public int SequenceId { get; set; }

        /// <summary>
        /// 数据
        /// </summary>
        public Memory<byte> Body { get; set; }

        public Memory<byte> ToMemory(byte[] array)
        {
            var res = new Memory<byte>(array, 0, PacketLength);

            BinaryPrimitives.WriteInt32BigEndian(res.Span, PacketLength);
            BinaryPrimitives.WriteInt16BigEndian(res.Span.Slice(4), HeaderLength);
            BinaryPrimitives.WriteInt16BigEndian(res.Span.Slice(6), ProtocolVersion);
            BinaryPrimitives.WriteInt32BigEndian(res.Span.Slice(8), (int)Operation);
            BinaryPrimitives.WriteInt32BigEndian(res.Span.Slice(12), SequenceId);
            Body.CopyTo(res.Slice(HeaderLength));

            return res;
        }

        public void ReadDanMu(Memory<byte> buffer)
        {
            HeaderLength = BinaryPrimitives.ReadInt16BigEndian(buffer.Span);
            ProtocolVersion = BinaryPrimitives.ReadInt16BigEndian(buffer.Span.Slice(2));
            Operation = (Operation)BinaryPrimitives.ReadInt32BigEndian(buffer.Span.Slice(4));
            SequenceId = BinaryPrimitives.ReadInt32BigEndian(buffer.Span.Slice(8));
            Body = buffer.Slice(HeaderLength - 4, PacketLength - HeaderLength);
        }

        public ReadOnlySequence<byte> ReadDanMu(in ReadOnlySequence<byte> sequence)
        {
            var length = sequence.Length;
            if (length < 16)
            {
                return sequence;
            }

            var reader = new SequenceReader<byte>(sequence);

            reader.TryReadBigEndian(out int packetLength);
            if (packetLength > length)
            {
                return sequence;
            }

            reader.TryReadBigEndian(out short headerLength);
            reader.TryReadBigEndian(out short version);
            reader.TryReadBigEndian(out int operation);
            reader.TryReadBigEndian(out int seqId);

            PacketLength = packetLength;
            HeaderLength = headerLength;
            ProtocolVersion = version;
            Operation = (Operation)operation;
            SequenceId = seqId;

            // TODO .NET 5.0
            Body = sequence.Slice(HeaderLength, PacketLength - HeaderLength).ToArray();

            return sequence.Slice(packetLength);
        }
    }
}