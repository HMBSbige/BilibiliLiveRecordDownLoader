using Splat;

namespace BilibiliLiveRecordDownLoader.Application.Tests;

public static class TestSettings
{
	[Before(TestDiscovery)]
	public static void Configure(BeforeTestDiscoveryContext context)
	{
		context.Settings.Execution.FailOnUnobservedTaskExceptions = true;
	}

	// 房间列表项读取 TimeProvider 时走全局定位器
	[Before(Assembly)]
	public static void RegisterServices()
	{
		AppLocator.CurrentMutable.RegisterConstant(TimeProvider.System);
	}
}
