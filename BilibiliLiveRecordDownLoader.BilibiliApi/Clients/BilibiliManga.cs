using BilibiliApi.Model.Manga.GetClockInInfo;
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
		private const string MangaBaseAddress = @"https://manga.bilibili.com/";

		public async Task<GetClockInInfoMessage> GetMangaClockInInfoAsync(string accessToken, CancellationToken token = default)
		{
			const string url = MangaBaseAddress + @"twirp/activity.v1.Activity/GetClockInInfo";
			var pair = new Dictionary<string, string>
			{
				{@"access_key", accessToken}
			};
			var response = await PostAsync(url, pair, true, token);
			var message = await response.Content.ReadFromJsonAsync<GetClockInInfoMessage>(cancellationToken: token);
			if (message is null)
			{
				throw new HttpRequestException(@"获取签到状态失败");
			}
			return message;
		}

		/// <summary>
		/// 签到成功返回 true，重复签到返回 null
		/// </summary>
		public async Task<bool?> MangaClockInAsync(string accessToken, CancellationToken token = default)
		{
			const string url = MangaBaseAddress + @"twirp/activity.v1.Activity/ClockIn";
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"access_key", accessToken}
			};
			var response = await PostAsync(url, pair, true, token);
			var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
			if (root.TryGetProperty(@"code", out var codeProperty)
				&& codeProperty.ValueKind == JsonValueKind.Number
				&& codeProperty.TryGetInt32(out var code)
				&& code == 0)
			{
				return true;
			}

			if (root.TryGetProperty(@"msg", out var msgProperty))
			{
				var msg = msgProperty.GetString();
				if (msg == @"clockin clockin is duplicate")
				{
					return null;
				}

				throw new HttpRequestException(msg);
			}
			return false;
		}

		public async Task<bool?> ShareComicAsync(string accessToken, CancellationToken token = default)
		{
			const string url = MangaBaseAddress + @"twirp/activity.v1.Activity/ShareComic";
			var pair = new Dictionary<string, string>
			{
				{@"platform", @"android"},
				{@"access_key", accessToken}
			};
			var response = await PostAsync(url, pair, true, token);
			var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
			if (root.TryGetProperty(@"code", out var codeProperty)
				&& codeProperty.ValueKind == JsonValueKind.Number
				&& codeProperty.TryGetInt32(out var code))
			{
				switch (code)
				{
					case 0:
					{
						return true;
					}
					case 1:
					{
						return null;
					}
				}
			}

			if (root.TryGetProperty(@"msg", out var msgProperty))
			{
				throw new HttpRequestException(msgProperty.GetString());
			}

			return false;
		}
	}
}
