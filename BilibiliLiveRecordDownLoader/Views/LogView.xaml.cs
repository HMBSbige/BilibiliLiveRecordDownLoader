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

			this.WhenActivated(d =>
			{
				LogTextBox.Clear();
				Constants.SubjectMemorySink.LogSubject.ObserveOnDispatcher().Subscribe(str => LogTextBox.AppendText(str)).DisposeWith(d);
			});
		}
	}
}
