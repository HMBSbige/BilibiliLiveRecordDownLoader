using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders
{
	public class StreamId : IBytesStruct
	{
		public int Size => 3;

		public Memory<byte> ToMemory(Memory<byte> array)
		{
			var res = array.Slice(0, Size);
			res.Span.Fill(0);
			return res;
		}

		public void Read(Span<byte> buffer) { }
	}
}
