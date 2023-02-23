using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Serilog;
using Serilog.Events;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;

namespace BilibiliLiveRecordDownLoader.Services;

public static class DI
{
	private static readonly SubjectMemorySink MemorySink = new(Constants.OutputTemplate);

	public static T GetRequiredService<T>()
	{
		var service = Locator.Current.GetService<T>();

		if (service is null)
		{
			throw new InvalidOperationException($@"No service for type {typeof(T)} has been registered.");
		}

		return service;
	}

	public static ILogger<T> GetLogger<T>()
	{
		return GetRequiredService<ILogger<T>>();
	}

	public static void CreateLogger()
	{
		Log.Logger = new LoggerConfiguration()
#if DEBUG
			.MinimumLevel.Debug()
			.WriteTo.Async(c => c.Debug(outputTemplate: Constants.OutputTemplate))
#else
				.MinimumLevel.Information()
#endif
			.MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
			.Enrich.FromLogContext()
			.WriteTo.Async(c => c.File(Constants.LogFile,
				outputTemplate: Constants.OutputTemplate,
				rollingInterval: RollingInterval.Day,
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: Constants.MaxLogFileSize))
			.WriteTo.Async(c => c.Sink(MemorySink))
			.CreateLogger();
	}

	public static void Register()
	{
		var services = new ServiceCollection();

		services.UseMicrosoftDependencyResolver();
		Locator.CurrentMutable.InitializeSplat();
		Locator.CurrentMutable.InitializeReactiveUI(RegistrationNamespace.Wpf);

		ConfigureServices(services);
	}

	private static IServiceCollection ConfigureServices(IServiceCollection services)
	{
		services.AddViewModels()
			.AddViews()
			.AddDanmuClients()
			.AddConfig()
			.AddDynamicData()
			.AddFlvProcessor()
			.AddStartupService()
			.AddGlobalTaskQueue()
			.AddBilibiliApiClient()
			.AddRecorder()
			.AddLogging(c => c.AddSerilog());

		services.TryAddSingleton(MemorySink);

		return services;
	}
}
