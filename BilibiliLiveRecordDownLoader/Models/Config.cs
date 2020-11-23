using DynamicData.Kernel;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;

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
		private string _userAgent = string.Empty;
		private string _cookie = string.Empty;
		private List<RoomStatus> _rooms = new();

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

		public string UserAgent
		{
			get => _userAgent;
			set => this.RaiseAndSetIfChanged(ref _userAgent, value);
		}

		public string Cookie
		{
			get => _cookie;
			set => this.RaiseAndSetIfChanged(ref _cookie, value);
		}

		public List<RoomStatus> Rooms
		{
			get => _rooms;
			set
			{
				this.RaiseAndSetIfChanged(ref _rooms, value);
				_rooms = _rooms.Distinct().AsList();
			}
		}

		#endregion

		public void Clone(Config config)
		{
			RoomId = config.RoomId;
			MainDir = config.MainDir;
			DownloadThreads = config.DownloadThreads;
			IsCheckUpdateOnStart = config.IsCheckUpdateOnStart;
			IsCheckPreRelease = config.IsCheckPreRelease;
			MainWindowsWidth = config.MainWindowsWidth;
			MainWindowsHeight = config.MainWindowsHeight;
			UserAgent = config.UserAgent;
			Cookie = config.Cookie;
			Rooms = config.Rooms;
		}
	}
}
