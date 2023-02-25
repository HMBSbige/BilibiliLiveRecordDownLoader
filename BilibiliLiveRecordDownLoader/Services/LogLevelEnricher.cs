using Serilog.Core;
using Serilog.Events;

namespace BilibiliLiveRecordDownLoader.Services;

public class LogLevelEnricher : ILogEventEnricher
{
	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		string levelName = logEvent.Level switch
		{
			LogEventLevel.Debug => @"调试",
			LogEventLevel.Error => @"错误",
			LogEventLevel.Fatal => @"致命",
			LogEventLevel.Information => @"信息",
			LogEventLevel.Verbose => @"详细",
			LogEventLevel.Warning => @"警告",
			_ => @"未知"
		};

		logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(@"LevelCN", levelName));
	}
}
