using BilibiliLiveRecordDownLoader.JsonConverters;
using DynamicData.Kernel;
using ModernWpf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.Models
{
	[JsonConverter(typeof(GlobalConfigConverter))]
	public class Config : ReactiveObject
	{
		#region 默认值

		public const long DefaultRoomId = 732;
		public static string DefaultMainDir => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
		public const byte DefaultDownloadThreads = 8;
		public const bool DefaultIsCheckUpdateOnStart = true;
		public const double DefaultMainWindowsWidth = 1280.0;
		public const double DefaultMainWindowsHeight = 720.0;
		public const string DefaultUserAgent = @"";
		public const string DefaultCookie = @"";
		public const bool DefaultIsAutoConvertMp4 = true;
		public const bool DefaultIsUseProxy = true;
		public const ElementTheme DefaultTheme = ElementTheme.Default;

		#endregion

		#region 属性

		[DefaultValue(DefaultRoomId)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public long RoomId { get; set; } = DefaultRoomId;

		[Reactive]
		public string MainDir { get; set; } = DefaultMainDir;

		[DefaultValue(DefaultDownloadThreads)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public byte DownloadThreads
		{
			get => Math.Clamp(_downloadThreads, (byte)1, (byte)128);
			set => this.RaiseAndSetIfChanged(ref _downloadThreads, value);
		}
		private byte _downloadThreads = DefaultDownloadThreads;

		[DefaultValue(DefaultIsCheckUpdateOnStart)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public bool IsCheckUpdateOnStart { get; set; } = DefaultIsCheckUpdateOnStart;

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public bool IsCheckPreRelease { get; set; }

		[DefaultValue(DefaultMainWindowsWidth)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public double MainWindowsWidth { get; set; } = DefaultMainWindowsWidth;

		[DefaultValue(DefaultMainWindowsHeight)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public double MainWindowsHeight { get; set; } = DefaultMainWindowsHeight;

		[DefaultValue(DefaultUserAgent)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public string UserAgent { get; set; } = DefaultUserAgent;

		[DefaultValue(DefaultCookie)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public string Cookie { get; set; } = DefaultCookie;

		private List<RoomStatus> _rooms = new();
		public List<RoomStatus> Rooms
		{
			get => _rooms;
			set
			{
				this.RaiseAndSetIfChanged(ref _rooms, value);
				_rooms = _rooms.Distinct().AsList();
			}
		}

		[DefaultValue(DefaultIsAutoConvertMp4)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public bool IsAutoConvertMp4 { get; set; } = DefaultIsAutoConvertMp4;

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public bool IsDeleteAfterConvert { get; set; }

		[DefaultValue(DefaultIsUseProxy)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public bool IsUseProxy { get; set; } = DefaultIsUseProxy;

		[DefaultValue(DefaultTheme)]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		[Reactive]
		public ElementTheme Theme { get; set; } = DefaultTheme;

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
