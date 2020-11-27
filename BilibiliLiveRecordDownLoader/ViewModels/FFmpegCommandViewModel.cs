using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class FFmpegCommandViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"FFmpegCommand";
		public IScreen HostScreen { get; }

		#region 字段

		private string _startTime = @"00:00:00.000";

		#endregion

		#region 属性

		public string StartTime
		{
			get => _startTime;
			set => this.RaiseAndSetIfChanged(ref _startTime, value);
		}

		#endregion

		private readonly ILogger _logger;

		public FFmpegCommandViewModel(
				IScreen hostScreen,
				ILogger<FFmpegCommandViewModel> logger)
		{
			HostScreen = hostScreen;
			_logger = logger;
		}

	}
}
