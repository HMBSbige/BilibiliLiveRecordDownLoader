using BilibiliApi.Clients;
using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.FFmpeg;
using BilibiliLiveRecordDownLoader.JsonConverters;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using Microsoft;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace BilibiliLiveRecordDownLoader.Models;

[JsonConverter(typeof(RoomStatusConverter))]
public class RoomStatus : ReactiveObject
{
	private readonly ILogger<RoomStatus> _logger;
	private readonly BilibiliApiClient _apiClient;
	private readonly Config _config;
	private readonly TaskListViewModel _taskList;

	private IDanmuClient? _danmuClient;
	private IDisposable? _httpMonitor;
	private IDisposable? _statusMonitor;
	private IDisposable? _enableMonitor;
	private IDisposable? _titleMonitor;
	private IDisposable? _scope;
	private CancellationTokenSource _recordCts = new();

	#region 默认值

	public const bool DefaultIsEnable = true;
	public const double DefaultDanMuReconnectLatency = 2.0;
	public const double DefaultHttpCheckLatency = 300.0;
	public const double DefaultStreamReconnectLatency = 6.0;
	public const double DefaultStreamConnectTimeout = 5.0;
	public const double DefaultStreamTimeout = 10.0;
	public const DanmuClientType DefaultClientType = DanmuClientType.SecureWebsocket;
	public const Qn DefaultQn = Qn.原画;
	public const RecorderType DefaultRecorderType = RecorderType.Default;

	#endregion

	#region 属性

	/// <summary>
	/// 是否启用录制
	/// </summary>
	[DefaultValue(DefaultIsEnable)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public bool IsEnable { get; set; } = true;

	/// <summary>
	/// 短号
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public long ShortId { get; set; }

	/// <summary>
	/// 房间号
	/// </summary>
	[Reactive]
	public long RoomId { get; set; } = 732;

	/// <summary>
	/// 主播名
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public string? UserName { get; set; }

	/// <summary>
	/// 直播间标题
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public string? Title { get; set; }

	/// <summary>
	/// 直播状态
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public LiveStatus LiveStatus { get; set; } = LiveStatus.未知;

	/// <summary>
	/// 录制状态
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public RecordStatus RecordStatus { get; set; }

	/// <summary>
	/// 是否开播提醒
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public bool IsNotify { get; set; }

	/// <summary>
	/// 弹幕重连间隔
	/// 单位 秒
	/// </summary>
	[DefaultValue(DefaultDanMuReconnectLatency)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public double DanMuReconnectLatency { get; set; } = DefaultDanMuReconnectLatency;

	/// <summary>
	/// Http 开播检查间隔
	/// 单位 秒
	/// </summary>
	[DefaultValue(DefaultHttpCheckLatency)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public double HttpCheckLatency { get; set; } = DefaultHttpCheckLatency;

	/// <summary>
	/// 直播重连间隔
	/// 单位 秒
	/// </summary>
	[DefaultValue(DefaultStreamReconnectLatency)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public double StreamReconnectLatency { get; set; } = DefaultStreamReconnectLatency;

	/// <summary>
	/// 直播连接超时
	/// 单位 秒
	/// </summary>
	[DefaultValue(DefaultStreamConnectTimeout)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public double StreamConnectTimeout { get; set; } = DefaultStreamConnectTimeout;

	/// <summary>
	/// 直播流超时
	/// 单位 秒
	/// </summary>
	[DefaultValue(DefaultStreamTimeout)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public double StreamTimeout { get; set; } = DefaultStreamTimeout;

	/// <summary>
	/// 速度
	/// </summary>
	[JsonIgnore]
	[Reactive]
	public string Speed { get; set; } = string.Empty;

	/// <summary>
	/// 弹幕服务器类型
	/// </summary>
	[DefaultValue(DefaultClientType)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public DanmuClientType ClientType { get; set; } = DefaultClientType;

	/// <summary>
	/// qn 参数
	/// </summary>
	[DefaultValue(DefaultQn)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public Qn Qn { get; set; } = DefaultQn;

	/// <summary>
	/// 录制方式
	/// </summary>
	[DefaultValue(DefaultRecorderType)]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public RecorderType RecorderType { get; set; } = DefaultRecorderType;

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public bool? IsAutoConvertMp4 { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	[Reactive]
	public bool? IsDeleteAfterConvert { get; set; }

	#endregion

	public RoomStatus()
	{
		_logger = DI.GetLogger<RoomStatus>();
		_config = DI.GetRequiredService<Config>();
		_apiClient = DI.GetRequiredService<BilibiliApiClient>();
		_taskList = DI.GetRequiredService<TaskListViewModel>();
	}

	#region ApiRequest

	public async Task GetRoomInfoDataAsync(CancellationToken token)
	{
		RoomInfoMessage.RoomInfoData data = await _apiClient.GetRoomInfoDataAsync(RoomId, token);
		CopyFromRoomInfoData(data);
	}

	public async Task RefreshStatusAsync(CancellationToken token)
	{
		try
		{
			await GetRoomInfoDataAsync(token);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"刷新房间状态出错");
		}
	}

	#endregion

	#region Start

	public void Start()
	{
		StartMonitor();
	}

	private void StartMonitor()
	{
		_scope = _logger.BeginScope($@"开始监控房间 {{{LoggerProperties.RoomIdPropertyName}}}", RoomId);
		_statusMonitor = this.WhenAnyValue(x => x.LiveStatus).ObserveOn(RxApp.TaskpoolScheduler).Subscribe(_ => StatusUpdatedAsync().Forget());
		_enableMonitor = this.WhenAnyValue(x => x.IsEnable).ObserveOn(RxApp.TaskpoolScheduler).Subscribe(_ => EnableUpdatedAsync().Forget());
		this.RaisePropertyChanged(nameof(LiveStatus));
		_titleMonitor = this.WhenAnyValue(x => x.Title).Subscribe(title =>
		{
			if (title is not null)
			{
				_logger.LogInformation(@"[TitleChanged] {title}", title);
			}
		});
		this.RaisePropertyChanged(nameof(Title));
		BuildDanmuClientAsync().Forget();
		BuildHttpCheckMonitor();
	}

	private void BuildHttpCheckMonitor()
	{
		_httpMonitor = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(HttpCheckLatency)).Subscribe(_ => RefreshStatusAsync(default).Forget());
	}

	private async ValueTask BuildDanmuClientAsync()
	{
		_danmuClient = ClientType switch
		{
			DanmuClientType.TCP => DI.GetRequiredService<TcpDanmuClient>(),
			DanmuClientType.Websocket => DI.GetRequiredService<WsDanmuClient>(),
			_ => DI.GetRequiredService<WssDanmuClient>()
		};
		_danmuClient.RetryInterval = TimeSpan.FromSeconds(DanMuReconnectLatency);
		_danmuClient.RoomId = RoomId;

		_danmuClient.Received.Subscribe(ParseDanmu);
		await _danmuClient.StartAsync();
	}

	private async Task StartRecordAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (LiveStatus is LiveStatus.直播)
			{
				try
				{
					RecordStatus = RecordStatus.启动中;
					cancellationToken.ThrowIfCancellationRequested();

					RecorderType type = RecorderType is RecorderType.Default ? _config.RecorderType : RecorderType;
					if (type is RecorderType.Default)
					{
						type = RecorderType.HttpFlv;
					}

					Uri[] uri = type switch
					{
						RecorderType.HttpFlv => new[] { await _apiClient.GetRoomStreamUriAsync(RoomId, (long)Qn, cancellationToken) },
						RecorderType.HlsTs => await _apiClient.GetRoomHlsUriAsync(RoomId, @"TS", (long)Qn, cancellationToken),
						RecorderType.HlsfMP4_FFmpeg => await _apiClient.GetRoomHlsUriAsync(RoomId, @"fMP4", (long)Qn, cancellationToken),
						_ => throw Assumes.NotReachable()
					};

					_logger.LogInformation(@"直播流：{uri}", (object)uri);

					await using ILiveStreamRecorder recorder = type switch
					{
						RecorderType.HttpFlv => DI.GetRequiredService<HttpFlvLiveStreamRecorder>(),
						RecorderType.HlsTs => DI.GetRequiredService<HttpLiveStreamRecorder>(),
						RecorderType.HlsfMP4_FFmpeg => DI.GetRequiredService<FFmpegLiveStreamRecorder>(),
						_ => throw Assumes.NotReachable()
					};

					recorder.Client.Timeout = TimeSpan.FromSeconds(StreamConnectTimeout);
					recorder.RoomId = RoomId;

					Task waitReconnect = Task.Delay(TimeSpan.FromSeconds(StreamReconnectLatency), cancellationToken);
					try
					{
						await recorder.InitializeAsync(uri, cancellationToken);
					}
					catch (Exception ex)
					{
						switch (ex)
						{
							case TaskCanceledException:
							{
								_logger.LogInformation(@"尝试下载直播流超时");
								break;
							}
							case HttpRequestException { StatusCode: not null } e:
							{
								_logger.LogInformation(@"尝试下载直播流时服务器返回了 {statusCode}", e.StatusCode);
								break;
							}
							case HttpRequestException:
							{
								_logger.LogInformation(@"尝试下载直播流时发生错误 {message}", ex.Message);
								break;
							}
							default:
							{
								_logger.LogError(ex, @"尝试下载直播流时发生错误");
								break;
							}
						}
						await waitReconnect;
						continue;
					}

					RecordStatus = RecordStatus.录制中;

					string filePath = Path.Combine(_config.MainDir, RoomId.ToString(CultureInfo.InvariantCulture), DateTime.Now.ToString(@"yyyyMMdd_HHmmss"));

					_logger.LogInformation(@"开始录制：{filePath}", filePath);

					try
					{
						DateTime lastDataReceivedTime = DateTime.Now;
						using CancellationTokenSource recordStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						using IDisposable speedMonitor = recorder.CurrentSpeed.Subscribe(b =>
						{
							Speed = b.ToHumanBytesString() + @"/s";
							DateTime now = DateTime.Now;
							if (b > 0.0)
							{
								lastDataReceivedTime = now;
							}
							else if (now - lastDataReceivedTime > TimeSpan.FromSeconds(StreamTimeout))
							{
								if (LiveStatus is LiveStatus.直播)
								{
									_logger.LogWarning(@"录播不稳定，即将尝试重连");
								}
								// ReSharper disable once AccessToDisposedClosure
								recordStreamCts.Cancel();
							}
						});
						await recorder.DownloadAsync(filePath, recordStreamCts.Token);
					}
					finally
					{
						_logger.LogInformation(@"录制结束");

						recorder.WriteToFileTask?.ContinueWith(task => ProcessVideoAsync(task.Result).Forget(), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current).Forget();
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					// manually canceled
					throw;
				}
				catch (OperationCanceledException) when (LiveStatus is not LiveStatus.直播)
				{
					break;
				}
				catch (OperationCanceledException ex)
				{
					_logger.LogError(ex, @"录制被取消");
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, @"下载直播流时发生错误");
				}
			}
			_logger.LogInformation(@"不再录制");
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_logger.LogInformation(@"录制已取消");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"录制出现错误");
		}
		finally
		{
			RecordStatus = RecordStatus.未录制;
			Speed = string.Empty;
		}
	}

	private async Task ProcessVideoAsync(string file)
	{
		try
		{
			FileInfo fileInfo = new(file);
			if (!fileInfo.Exists)
			{
				return;
			}

			if (fileInfo.Length is 0)
			{
				FileUtils.DeleteWithoutException(fileInfo.FullName);
				return;
			}

			if (!(IsAutoConvertMp4 ?? _config.IsAutoConvertMp4))
			{
				return;
			}

			if (fileInfo.Extension.Equals(@".mp4", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}

			using FFmpegCommand ffmpeg = DI.GetRequiredService<FFmpegCommand>();
			string? version = await ffmpeg.GetVersionAsync();

			if (version is null)
			{
				return;
			}

			string mp4 = Path.ChangeExtension(file, @".mp4");
			string args = string.Format(Constants.FFmpegCopyConvert, file, mp4);

			FFmpegTaskViewModel task = new(args);
			await _taskList.AddTaskAsync(task, Path.GetPathRoot(mp4) ?? string.Empty);
			if (IsDeleteAfterConvert ?? _config.IsDeleteAfterConvert)
			{
				FileUtils.DeleteWithoutException(file);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"转封装 MP4 时发生错误");
		}
	}

	private async Task StartRecordAsync()
	{
		CancellationToken token;
		lock (this)
		{
			if (RecordStatus is not RecordStatus.未录制)
			{
				_logger.LogDebug(@"重复录制，已跳过");
				return;
			}

			RecordStatus = RecordStatus.启动中;
			_recordCts = new CancellationTokenSource();
			token = _recordCts.Token;
		}
		await StartRecordAsync(token);
	}

	#endregion

	#region Stop

	public void Stop()
	{
		StopMonitor();
		StopRecord();
	}

	private void StopMonitor()
	{
		_titleMonitor?.Dispose();
		_enableMonitor?.Dispose();
		_statusMonitor?.Dispose();
		_danmuClient?.Dispose();
		_httpMonitor?.Dispose();
		_scope?.Dispose();
	}

	private void StopRecord()
	{
		_recordCts.Cancel();
	}

	#endregion

	#region PropertyUpdated

	private void ParseDanmu(DanmuPacket packet)
	{
		try
		{
			if (packet.Operation != Operation.SendMsgReply)
			{
				return;
			}

			IDanmu? danMu = DanmuFactory.ParseJson(packet.Body);
			if (danMu is null)
			{
				return;
			}

			LiveStatus streamingStatus = danMu.IsStreaming();
			if (streamingStatus != LiveStatus.未知)
			{
				LiveStatus = streamingStatus;
			}

			string? title = danMu.TitleChanged();
			if (title is not null)
			{
				Title = title;
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"弹幕解析失败：{operation} {protocolVersion} {body}", packet.Operation, packet.ProtocolVersion, Encoding.UTF8.GetString(packet.Body));
		}
	}

	private async ValueTask StatusUpdatedAsync()
	{
		try
		{
			if (LiveStatus is not LiveStatus.未知)
			{
				_logger.LogInformation(@"直播状态：{liveStatus}", LiveStatus);
			}

			if (LiveStatus is LiveStatus.直播)
			{
				if (IsNotify)
				{
					MessageBus.Current.SendMessage(this);
				}
				if (IsEnable)
				{
					await StartRecordAsync();
				}
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"启动/停止录制出现错误");
		}
	}

	private async ValueTask EnableUpdatedAsync()
	{
		try
		{
			if (IsEnable)
			{
				if (LiveStatus == LiveStatus.直播)
				{
					await StartRecordAsync();
				}
			}
			else
			{
				StopRecord();
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"启动/停止录制出现错误");
		}
	}

	#endregion

	#region Clone

	private void CopyFromRoomInfoData(RoomInfoMessage.RoomInfoData roomData)
	{
		if (roomData.room_info is not null)
		{
			RoomId = roomData.room_info.room_id;
			ShortId = roomData.room_info.short_id;
			LiveStatus = (LiveStatus)roomData.room_info.live_status;
			Title = roomData.room_info.title;
		}

		if (roomData.anchor_info?.base_info is not null)
		{
			UserName = roomData.anchor_info.base_info.uname;
		}
	}

	public RoomStatus Clone()
	{
		return new RoomStatus
		{
			IsEnable = IsEnable,
			IsNotify = IsNotify,
			RoomId = RoomId,
			DanMuReconnectLatency = DanMuReconnectLatency,
			HttpCheckLatency = HttpCheckLatency,
			StreamReconnectLatency = StreamReconnectLatency,
			StreamTimeout = StreamTimeout,
			ClientType = ClientType,
			Qn = Qn,
			RecorderType = RecorderType,
			StreamConnectTimeout = StreamConnectTimeout,
			IsAutoConvertMp4 = IsAutoConvertMp4,
			IsDeleteAfterConvert = IsDeleteAfterConvert
		};
	}

	public void Update(RoomStatus room)
	{
		IsEnable = room.IsEnable;
		IsNotify = room.IsNotify;
		//RoomId = room.RoomId;

		if (!DanMuReconnectLatency.Equals(room.DanMuReconnectLatency))
		{
			DanMuReconnectLatency = room.DanMuReconnectLatency;
			if (_danmuClient is not null)
			{
				_danmuClient.RetryInterval = TimeSpan.FromSeconds(DanMuReconnectLatency);
			}
		}

		if (!HttpCheckLatency.Equals(room.HttpCheckLatency))
		{
			HttpCheckLatency = room.HttpCheckLatency;
			_httpMonitor?.Dispose();
			BuildHttpCheckMonitor();
		}

		StreamReconnectLatency = room.StreamReconnectLatency;
		StreamConnectTimeout = room.StreamConnectTimeout;
		StreamTimeout = room.StreamTimeout;

		if (ClientType != room.ClientType)
		{
			ClientType = room.ClientType;
			_danmuClient?.Dispose();
			BuildDanmuClientAsync().Forget();
		}

		Qn = room.Qn;
		RecorderType = room.RecorderType;

		IsAutoConvertMp4 = room.IsAutoConvertMp4;
		IsDeleteAfterConvert = room.IsDeleteAfterConvert;
	}

	#endregion

	#region Equals

	public override bool Equals(object? obj)
	{
		return obj is RoomStatus room && room.RoomId == RoomId;
	}

	public override int GetHashCode()
	{
		// ReSharper disable once NonReadonlyMemberInGetHashCode
		return HashCode.Combine(RoomId);
	}

	#endregion
}
