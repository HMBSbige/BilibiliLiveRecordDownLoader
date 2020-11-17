using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.Views
{
	public class MainScreen : ReactiveObject, IScreen
	{
		public RoutingState Router { get; } = new();
	}
}
