using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static UnitTest.TestConstants;

#pragma warning disable 8602
namespace UnitTest
{
	[TestClass]
	public class BilibiliLoginTest
	{
		private readonly BilibiliApiClient _apiClient = new(HttpClientUtils.BuildClientForBilibili(string.Empty, Cookie, new SocketsHttpHandler()));

		[TestMethod]
		public async Task GetHashTestAsync()
		{
			var data = await _apiClient.GetKeyAsync();
			Console.WriteLine(data.hash);
			Console.WriteLine(data.key);
			Assert.AreEqual(data.hash.Length, 16);
			Assert.IsTrue(data.key.StartsWith("-----BEGIN PUBLIC KEY-----\n"));
			Assert.IsTrue(data.key.EndsWith("\n-----END PUBLIC KEY-----\n"));
		}

		[TestMethod]
		public async Task LoginTestAsync()
		{
			var message = await _apiClient.LoginAsync(Username, Password);
			Console.WriteLine(message.data.token_info.access_token);
			Console.WriteLine(message.data.token_info.refresh_token);

			Assert.AreEqual(0, message.code);
			Console.WriteLine(Timestamp.GetTime(message.ts).ToLocalTime().ToString(@"yyyyMMdd_HHmmss"));

			//token_info
			Assert.IsTrue(message.data.token_info.mid > 0);
			Assert.AreEqual(32, message.data.token_info.access_token.Length);
			Assert.AreEqual(32, message.data.token_info.refresh_token.Length);
			Console.WriteLine(TimeSpan.FromSeconds(message.data.token_info.expires_in));

			//cookie_info
			var cookies = message.data.cookie_info.cookies;
			Assert.IsTrue(cookies.Length > 0);
			var names = cookies.Select(x => x.name).ToArray();
			Assert.IsTrue(names.Contains(@"bili_jct"));
			Assert.IsTrue(names.Contains(@"DedeUserID"));
			Assert.IsTrue(names.Contains(@"DedeUserID__ckMd5"));
			Assert.IsTrue(names.Contains(@"sid"));
			Assert.IsTrue(names.Contains(@"SESSDATA"));
		}

		[TestMethod]
		public async Task GetTokenInfoTestAsync()
		{
			var message = await _apiClient.GetTokenInfoAsync(AccessToken);

			Assert.AreEqual(0, message.code);
			Console.WriteLine(Timestamp.GetTime(message.ts).ToLocalTime().ToString(@"yyyyMMdd_HHmmss"));

			Assert.IsTrue(message.data.mid > 0);
			Assert.AreEqual(32, message.data.access_token.Length);
			Console.WriteLine(TimeSpan.FromSeconds(message.data.expires_in));
			Console.WriteLine(message.data.refresh);
		}

		[TestMethod]
		public async Task RevokeTestAsync()
		{
			Assert.IsFalse(await _apiClient.RevokeAsync(string.Empty));
			Assert.IsTrue(await _apiClient.RevokeAsync(AccessToken));
		}

		[TestMethod]
		public async Task RefreshTokenTestAsync()
		{
			var message = await _apiClient.RefreshTokenAsync(AccessToken, RefreshToken);
			Console.WriteLine(message.data.token_info.access_token);
			Console.WriteLine(message.data.token_info.refresh_token);

			Assert.AreEqual(0, message.code);
			Console.WriteLine(Timestamp.GetTime(message.ts).ToLocalTime().ToString(@"yyyyMMdd_HHmmss"));

			//token_info
			Assert.IsTrue(message.data.token_info.mid > 0);
			Assert.AreEqual(32, message.data.token_info.access_token.Length);
			Assert.AreEqual(32, message.data.token_info.refresh_token.Length);
			Console.WriteLine(TimeSpan.FromSeconds(message.data.token_info.expires_in));

			//cookie_info
			var cookies = message.data.cookie_info.cookies;
			Assert.IsTrue(cookies.Length > 0);
			var names = cookies.Select(x => x.name).ToArray();
			Assert.IsTrue(names.Contains(@"bili_jct"));
			Assert.IsTrue(names.Contains(@"DedeUserID"));
			Assert.IsTrue(names.Contains(@"DedeUserID__ckMd5"));
			Assert.IsTrue(names.Contains(@"sid"));
			Assert.IsTrue(names.Contains(@"SESSDATA"));
		}
	}
}
