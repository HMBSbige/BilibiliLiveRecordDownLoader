using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System.Buffers.Binary;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders;

public class FlvTimestamp : IBytesStruct
{
	#region Field

	public uint Data;

	#endregion

	public int Size => 4;

	public Memory<byte> ToMemory(Memory<byte> array)
	{
		var res = array[..Size];

		BinaryPrimitives.WriteUInt32BigEndian(res.Span, Data);

		var upper = res.Span[0];
		res.Span[0] = res.Span[1];
		res.Span[1] = res.Span[2];
		res.Span[2] = res.Span[3];
		res.Span[3] = upper;

		return res;
	}

	public void Read(Span<byte> buffer)
	{
		var a = BinaryPrimitives.ReadUInt32BigEndian(buffer);
		Data = ((a & 0xFF) << 24) | (a >> 8);
	}
}
