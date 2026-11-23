using BilibiliLiveRecordDownLoader.Configuration;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

// 平台初始化（选择渲染后端等）早于容器创建，日志管道要先建好，那时 Avalonia 的告警才记得下
internal static class AppLogging
{
	private const string OutputTemplate = $"[{{Timestamp:O}}] [{{Level:u3}}] [{{{LogProperties.RoomId}}}] {{Message:lj}}{{NewLine}}{{Exception}}";

	private static LogStoreSink? _store;

	public static LogStoreSink EnsureConfigured()
	{
		return _store ??= Configure();
	}

	private static LogStoreSink Configure()
	{
#if DEBUG
		SelfLog.Enable(static message => Debug.Print(message));
#endif

		LogStoreSink store = new();

		Log.Logger = new LoggerConfiguration()
#if DEBUG
			.MinimumLevel.Debug()
#else
			.MinimumLevel.Information()
#endif
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
			.Enrich.FromLogContext()
#if DEBUG
			.WriteTo.Async(static sinks => sinks.Debug(outputTemplate: OutputTemplate, formatProvider: CultureInfo.InvariantCulture))
#endif
			.WriteTo.Async
			(static sinks => sinks.File
				(
					Path.Combine(ConfigurationConsts.LogDirectory, $"{AppConsts.Name}.log"),
					outputTemplate: OutputTemplate,
					formatProvider: CultureInfo.InvariantCulture,
					fileSizeLimitBytes: 10 * 1024 * 1024,
					rollingInterval: RollingInterval.Day,
					rollOnFileSizeLimit: true,
					retainedFileCountLimit: 31
				)
			)
			.WriteTo.Sink(store)
			.CreateLogger();

		Avalonia.Logging.Logger.Sink = new AvaloniaLogSink();

		return store;
	}
}
