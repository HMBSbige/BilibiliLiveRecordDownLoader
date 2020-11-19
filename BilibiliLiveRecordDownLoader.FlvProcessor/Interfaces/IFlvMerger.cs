using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces
{
	public interface IFlvMerger : IAsyncDisposable, IProgress
	{
		public int BufferSize { get; init; }

		/// <summary>
		/// 输出 FLV 时是否使用异步
		/// </summary>
		public bool IsAsync { get; init; }

		/// <summary>
		/// 需要合并的 FLV
		/// </summary>
		public IEnumerable<string> Files { get; }

		/// <summary>
		/// 添加 FLV 文件
		/// </summary>
		public void Add(string path);

		/// <summary>
		/// 添加多个 FLV 文件
		/// </summary>
		public void AddRange(IEnumerable<string> path);

		/// <summary>
		/// 合并 FLV 到指定路径
		/// </summary>
		/// <param name="path">输出的 FLV 路径</param>
		/// <param name="token"></param>
		ValueTask MergeAsync(string path, CancellationToken token);
	}
}
