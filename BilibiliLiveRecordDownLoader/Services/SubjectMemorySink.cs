using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using DynamicData;
using ReactiveUI;
using Serilog.Core;
using Serilog.Events;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.Services;

public class SubjectMemorySink : ILogEventSink
{
	private readonly SourceList<LogModel> _list = new();
	public readonly ReadOnlyObservableCollection<LogModel> Logs;

	public SubjectMemorySink()
	{
		_list.Connect()
			.ObserveOn(RxApp.MainThreadScheduler)
			.Bind(out Logs)
			.Subscribe();
		_list.LimitSizeTo(100).Subscribe();
	}

	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		LogModel log = new()
		{
			Timestamp = logEvent.Timestamp,
			Level = logEvent.Level,
			Message = logEvent.RenderMessage()
		};

		if (logEvent.Exception is not null)
		{
			log.Message += Environment.NewLine + logEvent.Exception.Message;
		}

		if (logEvent.Properties.TryGetValue(LoggerProperties.RoomIdPropertyName, out LogEventPropertyValue? value)
			&& value is ScalarValue { Value: long id })
		{
			log.RoomId = id;
		}

		_list.Add(log);
	}
}
