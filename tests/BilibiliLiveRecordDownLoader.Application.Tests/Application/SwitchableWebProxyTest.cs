using BilibiliLiveRecordDownLoader.Application.Http;
using System.Net;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class SwitchableWebProxyTest
{
	private static readonly Uri Destination = new("https://api.live.bilibili.com/");

	[Test]
	public async Task ProxyFollowsTheSettingWhileRunningAsync()
	{
		WebProxy systemProxy = new("http://proxy.test:8080/");
		SwitchableWebProxy proxy = new(systemProxy);
		AppConfig config = AppConfig.CreateDefault();
		proxy.Follow(config);

		config.UseSystemProxy = true;
		(Uri? on, bool bypassedWhenOn) = (proxy.GetProxy(Destination), proxy.IsBypassed(Destination));
		config.UseSystemProxy = false;

		await Assert.That((on, bypassedWhenOn, proxy.GetProxy(Destination), proxy.IsBypassed(Destination))).IsEqualTo((systemProxy.Address, false, null, true));
	}
}
