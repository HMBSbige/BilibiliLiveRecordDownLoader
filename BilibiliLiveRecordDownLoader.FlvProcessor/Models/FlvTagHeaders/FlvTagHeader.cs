using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models.FlvTagHeaders
{
	public class FlvTagHeader : IBytesStruct
	{
		#region Field

		public FlvTagPayloadInfo PayloadInfo = new();

		/// <summary>
		/// 单位微秒
		/// </summary>
		public FlvTimestamp Timestamp = new();

		/// <summary>
		/// For first stream of same type set to NULL
		/// </summary>
		public StreamId StreamId = new();

		#endregion

		public int Size => 11;

		public Memory<byte> ToMemory(Memory<byte> array)
		{
			var res = array.Slice(0, Size);

			PayloadInfo.ToMemory(res);
			Timestamp.ToMemory(res[4..]);
			StreamId.ToMemory(res[8..]);

			return res;
		}

		public void Read(Span<byte> buffer)
		{
			PayloadInfo.Read(buffer);
			Timestamp.Read(buffer[4..]);
		}
	}
}
