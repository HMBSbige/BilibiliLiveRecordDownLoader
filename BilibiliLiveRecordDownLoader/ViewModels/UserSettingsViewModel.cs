using BilibiliLiveRecordDownLoader.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class UserSettingsViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"UserSettings";
		public IScreen HostScreen { get; }

		private readonly ILogger _logger;

		public readonly Config Config;

		public UserSettingsViewModel(
			IScreen hostScreen,
			ILogger<UserSettingsViewModel> logger,
			Config config)
		{
			HostScreen = hostScreen;
			_logger = logger;
			Config = config;
		}
	}
}
