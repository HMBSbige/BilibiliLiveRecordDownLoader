using BilibiliApi.Model.DanmuConf;
using BilibiliApi.Model.DanmuSend;
using System.Net.Http.Json;

namespace BilibiliApi.Clients;

public partial class BilibiliApiClient
{
	/// <summary>
	/// 获取弹幕服务器地址
	/// </summary>
	/// <param name="roomId">房间号（允许短号）</param>
	/// <param name="token"></param>
	public async Task<DanmuConfMessage?> GetDanmuConfAsync(long roomId, CancellationToken token = default)
	{
		string url = $@"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?id={roomId}";
		return await GetJsonAsync<DanmuConfMessage>(url, token);
	}

	/// <summary>
	/// 发送弹幕，需要登录
	/// </summary>
	/// <param name="roomId">房间号（不允许短号）</param>
	/// <param name="csrf">等同于 Cookie 中的 bili_jct</param>
	/// <param name="msg">发送弹幕的内容</param>
	/// <param name="rnd">通常为时间戳</param>
	/// <param name="color">颜色</param>
	/// <param name="fontSize">字体大小</param>
	/// <param name="token"></param>
	public async Task SendDanmuAsync(
		long roomId,
		string csrf,
		string msg = @"(｀・ω・´)",
		string rnd = @"114514",
		string color = @"16777215",
		string fontSize = @"25",
		CancellationToken token = default)
	{
		const string url = @"https://api.live.bilibili.com/msg/send";
		Dictionary<string, string> pair = new()
		{
			{ @"roomid", $@"{roomId}" },
			{ @"csrf", csrf },
			{ @"msg", msg },
			{ @"rnd", rnd },
			{ @"color", color },
			{ @"fontsize", fontSize }
		};
		HttpResponseMessage response = await PostAsync(url, pair, false, token);
		DanmuSendResponse? message = await response.Content.ReadFromJsonAsync<DanmuSendResponse>(token);

		if (message is null)
		{
			throw new HttpRequestException(@"发送弹幕失败");
		}

		if (message.code != 0)
		{
			throw new HttpRequestException(message.message);
		}
	}
}
