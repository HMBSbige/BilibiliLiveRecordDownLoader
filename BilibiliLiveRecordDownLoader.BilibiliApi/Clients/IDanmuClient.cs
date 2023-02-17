using BilibiliApi.Model.Danmu;
using Microsoft;

namespace BilibiliApi.Clients;

public interface IDanmuClient : IDisposableObservable
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
}
