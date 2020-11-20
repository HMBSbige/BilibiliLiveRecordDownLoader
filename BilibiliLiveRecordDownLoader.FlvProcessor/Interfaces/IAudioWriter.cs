using System;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
	internal interface IAudioWriter : IAsyncDisposable
	{
		int BufferSize { get; init; }
		/// <summary>
		/// 输出时是否使用异步
		/// </summary>
		bool IsAsync { get; init; }
		string Path { get; }
		void Write(Memory<byte> buffer, uint timestamp);
	}
}
