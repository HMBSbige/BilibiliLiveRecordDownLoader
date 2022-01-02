using BilibiliApi.Model.FansMedal;

namespace BilibiliApi.Clients;

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
		var url = $@"https://api.live.bilibili.com/xlive/app-ucenter/v1/user/GetMyMedals?page={page}&page_size={pageSize}";
		return await GetJsonAsync<LiveFansMedalMessage>(url, token);
	}

	/// <summary>
	/// 获取粉丝勋章列表
	/// </summary>
	public async Task<List<FansMedalList>> GetLiveFansMedalListAsync(CancellationToken token = default)
	{
		var res = new List<FansMedalList>();
		const long pageSize = 10;
		var totalPages = 2L;
		for (var i = 1L; i <= totalPages; ++i)
		{
			var message = await GetLiveFansMedalMessageAsync(i, pageSize, token);
			if (message is null)
			{
				throw new HttpRequestException(@"获取粉丝徽章列表错误");
			}
			if (message.code is not 0)
			{
				throw new HttpRequestException(message.message);
			}

			if (message.data?.items is null || message.data.page_info is null)
			{
				throw new HttpRequestException(@"获取粉丝徽章列表错误");
			}

			totalPages = message.data.page_info.total_page;
			res.AddRange(message.data.items);
		}
		return res;
	}

	#endregion

}
