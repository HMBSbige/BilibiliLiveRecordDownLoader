using BilibiliApi.Model.Danmu;
using System;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public interface IDanmuClient : IAsyncDisposable
	{
		/// <summary>
		/// 真实房间号
		/// </summary>
		long RoomId { get; set; }

		/// <summary>
		/// 连接失败重试间隔
		/// </summary>
		TimeSpan RetryInterval { get; set; }
		IObservable<DanmuPacket> Received { get; }
		BililiveApiClient? ApiClient { get; set; }
		ValueTask StartAsync();
		ValueTask StopAsync();
	}
}
