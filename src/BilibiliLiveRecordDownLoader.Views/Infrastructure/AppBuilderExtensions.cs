namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

public static class AppBuilderExtensions
{
	public static AppBuilder UseBilibiliLiveRecordDownLoader(this AppBuilder builder, AppStartupOptions startupOptions)
	{
		return builder.UseReactiveUIWithMicrosoftDependencyResolver
		(
			services => services.AddViews(startupOptions),
			withResolver: static provider =>
			{
				ArgumentNullException.ThrowIfNull(provider);
				provider.GetRequiredService<LogStoreSink>().Attach(provider.GetRequiredService<ILogStore>());
				provider.GetRequiredService<NotificationExceptionHandler>().Install();
			},
			withReactiveUIBuilder: static rxBuilder => rxBuilder.WithExceptionHandler(NotificationExceptionHandler.ExceptionSignal)
		);
	}
}
