using BilibiliLiveRecordDownLoader.FlvProcessor.Enums;
using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
	internal interface IVideoWriter
	{
		string Path { get; }
		void Write(Memory<byte> buffer, uint timestamp, FrameType type);
	}
}
