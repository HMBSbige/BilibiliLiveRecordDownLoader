using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System;
using System.IO;
using System.Reactive.Subjects;

namespace BilibiliLiveRecordDownLoader.Services
{
    public class SubjectMemorySink : ILogEventSink
    {
        private readonly ITextFormatter _textFormatter;

        public readonly ReplaySubject<string> LogSubject = new ReplaySubject<string>(100);

        public SubjectMemorySink(string outputTemplate)
        {
            _textFormatter = new MessageTemplateTextFormatter(outputTemplate);
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            var renderSpace = new StringWriter();
            _textFormatter.Format(logEvent, renderSpace);
            LogSubject.OnNext(renderSpace.ToString());
        }
    }
}
