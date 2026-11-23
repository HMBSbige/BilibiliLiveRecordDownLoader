namespace BilibiliLive.Tests;

public static class TestSettings
{
	[Before(TestDiscovery)]
	public static void Configure(BeforeTestDiscoveryContext context)
	{
		context.Settings.Execution.FailOnUnobservedTaskExceptions = true;
		context.Settings.Timeouts.DefaultTestTimeout = TimeSpan.FromSeconds(10);
	}
}
