using BilibiliApi.Model.Login.QrCode.GetLoginInfo;
using BilibiliApi.Model.Login.QrCode.GetLoginUrl;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Clients
{
	public partial class BilibiliApiClient
	{
		#region 检查登录状态

		public async Task<bool> CheckLoginStatusAsync(CancellationToken token = default)
		{
			const string url = @"https://api.bilibili.com/x/web-interface/nav/stat";
			var json = await GetJsonAsync<JsonElement>(url, token);
			return json.TryGetProperty(@"code", out var codeElement) && codeElement.TryGetInt64(out var code) && code == 0;
		}

		public async Task<long> GetUidAsync(CancellationToken token = default)
		{
			const string url = @"https://api.live.bilibili.com/User/getUserInfo";
			var json = await GetJsonAsync<JsonElement>(url, token);
			if (json.TryGetProperty(@"data", out var dataElement)
				&& dataElement.TryGetProperty(@"uid", out var uidElement)
				&& uidElement.TryGetInt64(out var uid))
			{
				return uid;
			}
			return 0;
		}

		#endregion

		#region 二维码地址及扫码密钥

		public async Task<GetLoginUrlMessage?> GetLoginUrlAsync(CancellationToken token = default)
		{
			const string url = @"https://passport.bilibili.com/qrcode/getLoginUrl";
			return await GetJsonAsync<GetLoginUrlMessage>(url, token);
		}

		public async Task<GetLoginUrlData> GetLoginUrlDataAsync(CancellationToken token = default)
		{
			var message = await GetLoginUrlAsync(token);
			if (message?.data?.url is null || message.code != 0 || message.data.oauthKey is null)
			{
				throw new HttpRequestException(@"获取二维码地址失败");
			}
			return message.data;
		}

		/// <summary>
		/// 获取登录信息
		/// </summary>
		/// <returns>Cookie</returns>
		public async Task<string> GetLoginInfoAsync(string oauthKey, CancellationToken token = default)
		{
			const string url = @"https://passport.bilibili.com/qrcode/getLoginInfo";
			IEnumerable<KeyValuePair<string?, string?>> pair = new[]
			{
				new KeyValuePair<string?, string?>(@"oauthKey", oauthKey)
			};
			using var content = new FormUrlEncodedContent(pair);
			var response = await PostAsync(url, content, token);
			var message = await response.Content.ReadFromJsonAsync<GetLoginInfoMessage>(cancellationToken: token);
			if (message is null)
			{
				throw new HttpRequestException(@"获取登录信息失败");
			}

			if (!message.status)
			{
				if (message.data.HasValue && message.data.Value.ValueKind == JsonValueKind.Number)
				{
					var i = message.data.Value.GetInt64();
					switch (i)
					{
						case -1:
						{
							throw new HttpRequestException(@"不存在该密钥");
						}
						case -2:
						{
							throw new HttpRequestException(@"密钥错误");
						}
						case -4:
						{
							throw new HttpRequestException(@"尚未扫描");
						}
						case -5:
						{
							throw new HttpRequestException(@"已扫描，尚未确认");
						}
					}
				}

				if (message.message is not null and not @"")
				{
					throw new HttpRequestException(message.message);
				}
			}

			if (!response.Headers.TryGetValues(@"Set-Cookie", out var values))
			{
				throw new HttpRequestException(@"无法获取 Cookie");
			}
			return values.ToCookie();
		}

		#endregion
	}
}
