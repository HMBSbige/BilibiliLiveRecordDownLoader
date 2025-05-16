using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.JsonConverters;
using DynamicData.Kernel;
using ModernWpf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.Models;

[JsonConverter(typeof(GlobalConfigConverter))]
public class Config : ReactiveObject
{
	#region 默认值

	public static string DefaultMainDir => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

	public const bool DefaultIsCheckUpdateOnStart = true;
	public const double DefaultMainWindowsWidth = 1280.0;
	public const double DefaultMainWindowsHeight = 720.0;

	public static string DefaultUserAgent => $@"Mozilla/5.0 {nameof(BilibiliLiveRecordDownLoader)}/{Utils.Utils.GetAppVersion()}";

	public const string DefaultCookie = @"";
	public const bool DefaultIsAutoConvertMp4 = false;
	public const bool DefaultIsUseProxy = false;
	public const ElementTheme DefaultTheme = ElementTheme.Default;
	public const RecorderType DefaultRecorderType = RecorderType.Auto;
	public const StreamHostRule DefaultStreamHostRule = StreamHostRule.FirstResponse;

	#endregion

	#region 属性

	[Reactive]
	public string MainDir { get; set; } = DefaultMainDir;

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

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[Reactive]
	public string? UserAgent { get; set; }

	[DefaultValue(DefaultCookie)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public string Cookie { get; set; } = DefaultCookie;

	private List<RoomStatus> _rooms = [];

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

	/// <summary>
	/// 全局默认录制方式
	/// </summary>
	[DefaultValue(DefaultRecorderType)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public RecorderType RecorderType { get; set; } = DefaultRecorderType;

	/// <summary>
	/// 编码优先级
	/// </summary>
	[DefaultValue(@"")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public string AutoRecordCodecOrder { get; set; } = string.Empty;

	/// <summary>
	/// 格式优先级
	/// </summary>
	[DefaultValue(@"")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public string AutoRecordFormatOrder { get; set; } = string.Empty;

	[DefaultValue(DefaultStreamHostRule)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public StreamHostRule StreamHostRule { get; set; } = DefaultStreamHostRule;

	#endregion

	/// <summary>
	/// 用于全局的 Handler
	/// </summary>
	[JsonIgnore]
	public HttpMessageHandler HttpHandler { get; set; } = new SocketsHttpHandler();

	public void Clone(Config config)
	{
		MainDir = config.MainDir;
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
		RecorderType = config.RecorderType;
		AutoRecordCodecOrder = config.AutoRecordCodecOrder;
		AutoRecordFormatOrder = config.AutoRecordFormatOrder;
		StreamHostRule = config.StreamHostRule;
	}
}
