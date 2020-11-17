using ReactiveUI;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public class GlobalViewModel : ReactiveObject
	{
		#region 字段

		private string? _imageUri;
		private string? _name;
		private long _uid;
		private long _level;
		private string? _diskUsageProgressBarText;
		private double _diskUsageProgressBarValue;
		private long _roomId;
		private long _shortRoomId;
		private long _recordCount;
		private bool _isLiveRecordBusy;
		private bool _triggerLiveRecordListQuery;
		private string? _updateStatus;

		#endregion

		#region 属性

		public string? ImageUri
		{
			get => _imageUri;
			set => this.RaiseAndSetIfChanged(ref _imageUri, value);
		}

		public string? Name
		{
			get => _name;
			set => this.RaiseAndSetIfChanged(ref _name, value);
		}

		public long Uid
		{
			get => _uid;
			set => this.RaiseAndSetIfChanged(ref _uid, value);
		}

		public long Level
		{
			get => _level;
			set => this.RaiseAndSetIfChanged(ref _level, value);
		}

		public string? DiskUsageProgressBarText
		{
			get => _diskUsageProgressBarText;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarText, value);
		}

		public double DiskUsageProgressBarValue
		{
			get => _diskUsageProgressBarValue;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarValue, value);
		}

		public long RoomId
		{
			get => _roomId;
			set => this.RaiseAndSetIfChanged(ref _roomId, value);
		}

		public long ShortRoomId
		{
			get => _shortRoomId;
			set => this.RaiseAndSetIfChanged(ref _shortRoomId, value);
		}

		public long RecordCount
		{
			get => _recordCount;
			set => this.RaiseAndSetIfChanged(ref _recordCount, value);
		}

		public bool IsLiveRecordBusy
		{
			get => _isLiveRecordBusy;
			set => this.RaiseAndSetIfChanged(ref _isLiveRecordBusy, value);
		}

		public bool TriggerLiveRecordListQuery
		{
			get => _triggerLiveRecordListQuery;
			set => this.RaiseAndSetIfChanged(ref _triggerLiveRecordListQuery, value);
		}

		public string? UpdateStatus
		{
			get => _updateStatus;
			set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
		}

		#endregion
	}
}
