using Avalonia.Logging;
using AvaloniaLogLevel = Avalonia.Logging.LogEventLevel;
using LogEventLevel = Serilog.Events.LogEventLevel;

namespace BilibiliLiveRecordDownLoader.Views.Infrastructure;

// Release 下 Trace 没有监听者，平台问题要进应用日志才查得到；绑定告警会刷满日志页，不写
internal sealed class AvaloniaLogSink : ILogSink
{
	private const string SourceContext = "Avalonia";

	public bool IsEnabled(AvaloniaLogLevel level, string area)
	{
		return level >= AvaloniaLogLevel.Warning && area is not LogArea.Binding;
	}

	public void Log(AvaloniaLogLevel level, string area, object? source, string messageTemplate)
	{
		Log(level, area, source, messageTemplate, []);
	}

	public void Log(AvaloniaLogLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
	{
		if (IsEnabled(level, area))
		{
			Serilog.Log.ForContext(Constants.SourceContextPropertyName, $"{SourceContext}.{area}").Write(ToSerilogLevel(level), messageTemplate, propertyValues);
		}
	}

	private static LogEventLevel ToSerilogLevel(AvaloniaLogLevel level)
	{
		return level switch
		{
			AvaloniaLogLevel.Verbose => LogEventLevel.Verbose,
			AvaloniaLogLevel.Debug => LogEventLevel.Debug,
			AvaloniaLogLevel.Information => LogEventLevel.Information,
			AvaloniaLogLevel.Warning => LogEventLevel.Warning,
			AvaloniaLogLevel.Error => LogEventLevel.Error,
			AvaloniaLogLevel.Fatal => LogEventLevel.Fatal
		};
	}
}
