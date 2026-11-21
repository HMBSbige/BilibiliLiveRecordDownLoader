namespace BilibiliLiveRecordDownLoader.Application.Tests;

public static class TestSettings
{
	[Before(TestDiscovery)]
	public static void Configure(BeforeTestDiscoveryContext context)
	{
		context.Settings.Execution.FailOnUnobservedTaskExceptions = true;
	}
}
