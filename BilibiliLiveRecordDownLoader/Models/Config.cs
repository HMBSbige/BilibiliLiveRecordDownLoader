using ReactiveUI;
using System;

namespace BilibiliLiveRecordDownLoader.Models
{
	public class Config : ReactiveObject
	{
		#region 字段

		private long _roomId = 732;
		private string _mainDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
		private byte _downloadThreads = 8;
		private bool _isCheckUpdateOnStart = true;
		private bool _isCheckPreRelease;
		private double _mainWindowsWidth = 1280;
		private double _mainWindowsHeight = 720;

		#endregion

		#region 属性

		public long RoomId
		{
			get => _roomId;
			set => this.RaiseAndSetIfChanged(ref _roomId, value);
		}

		public string MainDir
		{
			get => _mainDir;
			set => this.RaiseAndSetIfChanged(ref _mainDir, value);
		}

		public byte DownloadThreads
		{
			get => Math.Max((byte)1, _downloadThreads);
			set => this.RaiseAndSetIfChanged(ref _downloadThreads, value);
		}

		public bool IsCheckUpdateOnStart
		{
			get => _isCheckUpdateOnStart;
			set => this.RaiseAndSetIfChanged(ref _isCheckUpdateOnStart, value);
		}

		public bool IsCheckPreRelease
		{
			get => _isCheckPreRelease;
			set => this.RaiseAndSetIfChanged(ref _isCheckPreRelease, value);
		}

		public double MainWindowsWidth
		{
			get => _mainWindowsWidth;
			set => this.RaiseAndSetIfChanged(ref _mainWindowsWidth, value);
		}

		public double MainWindowsHeight
		{
			get => _mainWindowsHeight;
			set => this.RaiseAndSetIfChanged(ref _mainWindowsHeight, value);
		}

		#endregion
	}
}
