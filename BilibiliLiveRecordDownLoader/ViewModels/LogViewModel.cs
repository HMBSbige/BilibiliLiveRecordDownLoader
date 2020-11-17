using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612 // 类型中引用类型的为 Null 性与隐式实现的成员不匹配。
	public class LogViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612 // 类型中引用类型的为 Null 性与隐式实现的成员不匹配。
	{
		public string UrlPathSegment => @"Log";
		public IScreen HostScreen { get; }

		public LogViewModel(IScreen hostScreen)
		{
			HostScreen = hostScreen;
		}
	}
}
