using BilibiliApi.Enums;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace BilibiliApi.Model.Danmu
{
	public struct DanmuPacket
	{
		/// <summary>
		/// 消息总长度，HeaderLength + bytes(Body).Length
		/// </summary>
		public int PacketLength;

		/// <summary>
		/// 消息头长度，默认 16
		/// </summary>
		public short HeaderLength;

		/// <summary>
		/// 消息版本号
		/// </summary>
		public short ProtocolVersion;

		/// <summary>
		/// 消息类型
		/// </summary>
		public Operation Operation;

		/// <summary>
		/// 参数
		/// </summary>
		public int SequenceId;

		/// <summary>
		/// 数据
		/// </summary>
		public ReadOnlySequence<byte> Body;

		public void HeaderTo(Span<byte> span)
		{
			BinaryPrimitives.WriteInt32BigEndian(span, PacketLength);
			BinaryPrimitives.WriteInt16BigEndian(span.Slice(4), HeaderLength);
			BinaryPrimitives.WriteInt16BigEndian(span.Slice(6), ProtocolVersion);
			BinaryPrimitives.WriteInt32BigEndian(span.Slice(8), (int)Operation);
			BinaryPrimitives.WriteInt32BigEndian(span.Slice(12), SequenceId);
		}

		/// <summary>
		/// 读取弹幕
		/// </summary>
		/// <param name="sequence"></param>
		/// <returns>是否成功读取</returns>
		public bool ReadDanMu(ref ReadOnlySequence<byte> sequence)
		{
			var length = sequence.Length;
			if (length < 16)
			{
				return false;
			}

			var reader = new SequenceReader<byte>(sequence);

			if (!reader.TryReadBigEndian(out PacketLength))
			{
				goto Unreachable;
			}
			if (length < PacketLength)
			{
				return false;
			}

			if (reader.TryReadBigEndian(out HeaderLength) &&
				reader.TryReadBigEndian(out ProtocolVersion) &&
				reader.TryReadBigEndian(out int operation) &&
				reader.TryReadBigEndian(out SequenceId))
			{
				Operation = (Operation)operation;

				Body = reader.UnreadSequence.Slice(0, PacketLength - HeaderLength);

				sequence = sequence.Slice(PacketLength);
				return true;
			}
Unreachable:
			throw new InvalidDataException(@"错误的弹幕格式");
		}
	}
}
