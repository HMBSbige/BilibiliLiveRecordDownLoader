using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.Views
{
	public partial class FFmpegCommandView
	{
		public FFmpegCommandView(FFmpegCommandViewModel viewModel)
		{
			InitializeComponent();
			ViewModel = viewModel;

			this.WhenActivated(d =>
			{

			});
		}
	}
}
