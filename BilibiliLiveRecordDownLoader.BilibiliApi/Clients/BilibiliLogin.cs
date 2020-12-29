using BilibiliApi.Model.Login.Password.GetKey;
using BilibiliApi.Model.Login.Password.GetTokenInfo;
using BilibiliApi.Model.Login.Password.OAuth2;
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
			var pair = new Dictionary<string, string>
			{
				{@"oauthKey", oauthKey}
			};
			var response = await PostAsync(url, pair, false, token);
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

		#region App 登录

		private const string PassportBaseAddress = @"https://passport.bilibili.com/";

		/// <summary>
		/// 获取加密公钥及密码盐值
		/// </summary>
		public async Task<GetKeyData> GetKeyAsync(CancellationToken token = default)
		{
			const string url = PassportBaseAddress + @"api/oauth2/getKey";
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"}
			};
			var response = await PostAsync(url, pair, true, token);
			var message = await response.Content.ReadFromJsonAsync<GetKeyMessage>(cancellationToken: token);
			if (message?.code != 0
				|| message.data?.hash is null
				|| message.data.key is null)
			{
				throw new HttpRequestException(@"获取公钥失败");
			}

			return message.data;
		}

		public async Task<AppLoginMessage> LoginAsync(string username, string password, CancellationToken token = default)
		{
			const string url = PassportBaseAddress + @"api/v3/oauth2/login";
			var data = await GetKeyAsync(token);
			password = Rsa.Encrypt(data.key!, data.hash + password);
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"username", username},
				{@"password", password}
			};
			var response = await PostAsync(url, pair, true, token);
			var message = await response.Content.ReadFromJsonAsync<AppLoginMessage>(cancellationToken: token);
			if (message?.code != 0
				|| message.data?.cookie_info?.cookies is null
				|| message.data.token_info?.access_token is null)
			{
				throw new HttpRequestException(@"获取登录信息失败");
			}
			return message;
		}

		public async Task<TokenInfoMessage> GetTokenInfoAsync(string accessToken, CancellationToken token = default)
		{
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"access_token", accessToken}
			};
			using var body = await GetBody(pair, true);
			var para = await body.ReadAsStringAsync(token);
			var message = await GetJsonAsync<TokenInfoMessage>(PassportBaseAddress + @"api/oauth2/info?" + para, token);
			if (message is null)
			{
				throw new HttpRequestException(@"获取 Token 信息失败");
			}
			return message;
		}

		public async Task<bool> RevokeAsync(string accessToken, CancellationToken token = default)
		{
			const string url = PassportBaseAddress + @"api/oauth2/revoke";
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"access_token", accessToken}
			};
			var response = await PostAsync(url, pair, true, token);
			var message = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
			return message.TryGetProperty(@"code", out var codeElement) && codeElement.TryGetInt64(out var code) && code == 0;
		}

		public async Task<AppRefreshTokenMessage> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken token = default)
		{
			const string url = PassportBaseAddress + @"api/oauth2/refreshToken";
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"access_token", accessToken},
				{@"refresh_token", refreshToken}
			};
			var response = await PostAsync(url, pair, true, token);
			var message = await response.Content.ReadFromJsonAsync<AppRefreshTokenMessage>(cancellationToken: token);
			if (message is null)
			{
				throw new HttpRequestException(@"刷新 Token 失败");
			}
			return message;
		}

		#endregion
	}
}
