using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
	internal interface IAudioWriter
	{
		string Path { get; }
		void Write(Memory<byte> buffer, uint timestamp);
	}
}
