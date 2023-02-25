using Microsoft;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System.Globalization;
using System.IO;
using System.Reactive.Subjects;

namespace BilibiliLiveRecordDownLoader.Services;

public class SubjectMemorySink : ILogEventSink
{
	private readonly ITextFormatter _textFormatter;

	public readonly ReplaySubject<string> LogSubject = new(100);

	public SubjectMemorySink(string outputTemplate)
	{
		_textFormatter = new MessageTemplateTextFormatter(outputTemplate, CultureInfo.CurrentCulture);
	}

	public void Emit(LogEvent logEvent)
	{
		Requires.NotNull(logEvent, nameof(logEvent));

		using StringWriter writer = new();
		_textFormatter.Format(logEvent, writer);
		LogSubject.OnNext(writer.ToString());
	}
}
