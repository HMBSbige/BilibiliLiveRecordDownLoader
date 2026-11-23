namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

public static class AppBuilderExtensions
{
	public static AppBuilder UseBilibiliLiveRecordDownLoader(this AppBuilder builder, AppStartupOptions startupOptions)
	{
		AppLogging.EnsureConfigured();

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
