using DynamicData.Kernel;
using ModernWpf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.Models
{
	public class Config : ReactiveObject
	{
		#region 字段

		private byte _downloadThreads = 8;
		private List<RoomStatus> _rooms = new();

		#endregion

		#region 属性

		[Reactive]
		public long RoomId { get; set; } = 732;

		[Reactive]
		public string MainDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

		public byte DownloadThreads
		{
			get => Math.Max((byte)1, _downloadThreads);
			set => this.RaiseAndSetIfChanged(ref _downloadThreads, value);
		}

		[Reactive]
		public bool IsCheckUpdateOnStart { get; set; } = true;

		[Reactive]
		public bool IsCheckPreRelease { get; set; }

		[Reactive]
		public double MainWindowsWidth { get; set; } = 1280;

		[Reactive]
		public double MainWindowsHeight { get; set; } = 720;

		[Reactive]
		public string UserAgent { get; set; } = string.Empty;

		[Reactive]
		public string Cookie { get; set; } = string.Empty;

		public List<RoomStatus> Rooms
		{
			get => _rooms;
			set
			{
				this.RaiseAndSetIfChanged(ref _rooms, value);
				_rooms = _rooms.Distinct().AsList();
			}
		}

		[Reactive]
		public bool IsAutoConvertMp4 { get; set; } = true;

		[Reactive]
		public bool IsDeleteAfterConvert { get; set; }

		[Reactive]
		public bool IsUseProxy { get; set; } = true;

		[Reactive]
		public ElementTheme Theme { get; set; } = ElementTheme.Default;

		#endregion

		/// <summary>
		/// 用于全局的 Handler
		/// </summary>
		[JsonIgnore]
		public HttpMessageHandler HttpHandler { get; set; } = new SocketsHttpHandler();

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
			IsAutoConvertMp4 = config.IsAutoConvertMp4;
			IsDeleteAfterConvert = config.IsDeleteAfterConvert;
			IsUseProxy = config.IsUseProxy;
			Theme = config.Theme;
		}
	}
}
