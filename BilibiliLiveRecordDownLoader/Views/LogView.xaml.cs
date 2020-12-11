using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class LogView
	{
		public LogView(
			LogViewModel viewModel,
			SubjectMemorySink memorySink)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{
				LogTextBox.Clear();
				memorySink.LogSubject.ObserveOnDispatcher().Subscribe(str => LogTextBox.AppendText(str)).DisposeWith(d);
			});
		}
	}
}
