using BilibiliLiveRecordDownLoader.Utils;
using Microsoft;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Serilog;
using Serilog.Events;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Services;

public static class DI
{
	private static readonly SubjectMemorySink MemorySink = new();

	public static T GetRequiredService<T>()
	{
#if DEBUG
		if (DesignerProperties.IsInDesignModeProperty.GetMetadata(typeof(DependencyObject)).DefaultValue is true)
		{
			return default!;
		}
#endif

		T? service = Locator.Current.GetService<T>();

		Verify.Operation(service is not null, $@"No service for type {typeof(T)} has been registered.");

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
			.WriteTo.Async(c => c.Debug(outputTemplate: Constants.OutputTemplate, formatProvider: CultureInfo.CurrentCulture))
#else
			.MinimumLevel.Information()
#endif
			.MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
			.Enrich.FromLogContext()
			.WriteTo.Async(c => c.File(Constants.LogFile,
				outputTemplate: Constants.OutputTemplate,
				rollingInterval: RollingInterval.Day,
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: Constants.MaxLogFileSize,
				formatProvider: CultureInfo.CurrentCulture))
			.WriteTo.Async(c => c.Sink(MemorySink))
			.CreateLogger();
	}

	public static void Register()
	{
		ServiceCollection services = new();

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
		services.TryAddSingleton(MemorySink.Logs);

		return services;
	}
}
