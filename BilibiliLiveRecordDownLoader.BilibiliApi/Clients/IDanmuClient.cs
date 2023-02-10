using BilibiliApi.Model.Danmu;

namespace BilibiliApi.Clients;

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
	ValueTask StartAsync();
	ValueTask StopAsync();
}
