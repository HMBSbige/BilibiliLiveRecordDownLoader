using BilibiliLiveRecordDownLoader.Models;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public partial class RoomDialog
	{
		public RoomDialog(RoomStatus room)
		{
			DataContext = room;
			InitializeComponent();
		}
	}
}
