using BilibiliApi.Model.DanmuConf;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient
	{
		/// <summary>
		/// 获取弹幕服务器地址
		/// </summary>
		/// <param name="roomId">房间号（允许短号）</param>
		/// <param name="token"></param>
		/// <returns></returns>
		public async Task<DanmuConfMessage?> GetDanmuConfAsync(long roomId, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/room/v1/Danmu/getConf?room_id={roomId}";
			return await GetJsonAsync<DanmuConfMessage>(url, token);
		}
	}
}
