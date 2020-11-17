using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class LogViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"Log";
		public IScreen HostScreen { get; }

		public LogViewModel(IScreen hostScreen)
		{
			HostScreen = hostScreen;
		}
	}
}
