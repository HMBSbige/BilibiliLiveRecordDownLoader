using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Models;

namespace BilibiliLiveRecordDownLoader.Views.Dialogs
{
	public partial class RoomDialog
	{
		public RoomDialog(RoomDialogType type, RoomStatus room)
		{
			DataContext = room;
			InitializeComponent();
			if (type == RoomDialogType.Add)
			{
				Title = @"添加直播间";
				RoomIdNumberBox.IsEnabled = true;
			}
			else
			{
				Title = @"直播间设置";
				RoomIdNumberBox.IsEnabled = false;
			}
		}
	}
}
