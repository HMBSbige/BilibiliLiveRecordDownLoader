using BilibiliLiveRecordDownLoader.Models;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace BilibiliLiveRecordDownLoader.ViewModels;

public class LogViewModel : ReactiveObject, IRoutableViewModel
{
	public string UrlPathSegment => @"Log";
	public IScreen HostScreen { get; }

	public ReadOnlyObservableCollection<LogModel> Logs { get; }

	public LogViewModel(
		IScreen hostScreen,
		ReadOnlyObservableCollection<LogModel> logs
	)
	{
		HostScreen = hostScreen;
		Logs = logs;
	}
}
