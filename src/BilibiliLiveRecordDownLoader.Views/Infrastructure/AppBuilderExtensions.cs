namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

public static class AppBuilderExtensions
{
	public static AppBuilder UseBilibiliLiveRecordDownLoader(this AppBuilder builder, AppStartupOptions startupOptions)
	{
		AppLogging.EnsureConfigured();

		// Fluent2 的默认字体是 fonts:Inter，不注册就回落到系统默认字体
		return builder.UseReactiveUIWithMicrosoftDependencyResolver
		(
			services => services.AddViews(startupOptions),
			withResolver: static provider =>
			{
				ArgumentNullException.ThrowIfNull(provider);
				provider.GetRequiredService<GlobalExceptionHandler>().Install();
				provider.GetRequiredService<NotificationExceptionHandler>().Install();
			},
			withReactiveUIBuilder: static rxBuilder => rxBuilder.WithExceptionHandler(NotificationExceptionHandler.ExceptionSignal)
		);
	}
}
