using BilibiliApi.Model.FansMedal;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient
	{
		#region 粉丝勋章列表，需要登录

		/// <summary>
		/// 获取粉丝勋章列表信息
		/// </summary>
		/// <param name="page">页数</param>
		/// <param name="pageSize">每页大小</param>
		/// <param name="token"></param>
		public async Task<LiveFansMedalMessage?> GetLiveFansMedalMessageAsync(long page = 1, long pageSize = 10, CancellationToken token = default)
		{
			var url = $@"https://api.live.bilibili.com/fans_medal/v5/live_fans_medal/iApiMedal?page={page}&pageSize={pageSize}";
			return await GetJsonAsync<LiveFansMedalMessage>(url, token);
		}

		/// <summary>
		/// 获取粉丝勋章列表
		/// </summary>
		public async Task<List<FansMedalList>> GetLiveFansMedalListAsync(CancellationToken token = default)
		{
			var res = new List<FansMedalList>();
			const long pageSize = 100;
			var totalPages = 2L;
			for (var i = 1L; i <= totalPages; ++i)
			{
				var message = await GetLiveFansMedalMessageAsync(i, pageSize, token);
				if (message is null)
				{
					throw new HttpRequestException(@"获取粉丝徽章列表错误");
				}
				if (message.code != 0)
				{
					throw new HttpRequestException(message.message);
				}

				if (message.data?.fansMedalList is null || message.data.pageinfo is null)
				{
					throw new HttpRequestException(@"获取粉丝徽章列表错误");
				}

				totalPages = message.data.pageinfo.totalpages;
				res.AddRange(message.data.fansMedalList);
			}
			return res;
		}

		#endregion

	}
}
