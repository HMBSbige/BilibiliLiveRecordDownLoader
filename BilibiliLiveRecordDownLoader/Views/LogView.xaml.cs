using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class LogView
	{
		public LogView()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<LogViewModel>();
			var logServices = CreateLogService();

			this.WhenActivated(d =>
			{
				Observable.FromEventPattern(LogTextBox, nameof(LogTextBox.TextChanged)).Subscribe(_ =>
				{
					if (LogTextBox.LineCount > 2000)
					{
						logServices.Dispose();
						LogTextBox.Clear();
						logServices = CreateLogService();
					}
				}).DisposeWith(d);
				logServices.DisposeWith(d);
			});
		}

		private IDisposable CreateLogService()
		{
			return Constants.SubjectMemorySink.LogSubject
					.ObserveOnDispatcher()
					.Subscribe(str => LogTextBox.AppendText(str));
		}
	}
}
