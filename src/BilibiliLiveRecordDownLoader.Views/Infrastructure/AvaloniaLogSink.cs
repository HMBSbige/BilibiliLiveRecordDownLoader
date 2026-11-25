using Avalonia.Logging;
using System.Diagnostics.CodeAnalysis;
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

	[SuppressMessage("Usage", "CA2254", Justification = "转发 Avalonia 的消息模板，模板在 Avalonia 的调用处是常量")]
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
