using BilibiliApi.Clients;
using BilibiliApi.Enums;
using BilibiliApi.Model.Danmu;
using BilibiliApi.Model.RoomInfo;
using BilibiliApi.Utils;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.Http.Clients;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Splat;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Models
{
	public class RoomStatus : ReactiveObject
	{
		private readonly ILogger _logger;
		private readonly BililiveApiClient _apiClient;
		private readonly Config _config;
		private readonly TaskListViewModel _taskList;

		private IDanmuClient? _danmuClient;
		private IDisposable? _httpMonitor;
		private IDisposable? _statusMonitor;
		private IDisposable? _enableMonitor;
		private IDisposable? _titleMonitor;
		private CancellationTokenSource _recordCts = new();
		private CancellationToken _token => _recordCts.Token;

		#region 字段

		private bool _isEnable = true;
		private long _shortId;
		private long _roomId = 732;
		private string? _userName;
		private string? _title;
		private LiveStatus _liveStatus = LiveStatus.未知;
		private RecordStatus _recordStatus;
		private bool _isNotify;
		private double _danMuReconnectLatency = 2.0;
		private double _httpCheckLatency = 300.0;
		private double _streamReconnectLatency = 6.0;
		private double _streamConnectTimeout = 3.0;
		private double _streamTimeout = 5.0;
		private string _speed = string.Empty;
		private DanmuClientType _clientType = DanmuClientType.SecureWebsocket;
		private Qn _qn = Qn.原画;

		#endregion

		#region 属性

		/// <summary>
		/// 是否启用录制
		/// </summary>
		public bool IsEnable
		{
			get => _isEnable;
			set => this.RaiseAndSetIfChanged(ref _isEnable, value);
		}

		/// <summary>
		/// 短号
		/// </summary>
		[JsonIgnore]
		public long ShortId
		{
			get => _shortId;
			set => this.RaiseAndSetIfChanged(ref _shortId, value);
		}

		/// <summary>
		/// 房间号
		/// </summary>
		public long RoomId
		{
			get => _roomId;
			set => this.RaiseAndSetIfChanged(ref _roomId, value);
		}

		/// <summary>
		/// 主播名
		/// </summary>
		[JsonIgnore]
		public string? UserName
		{
			get => _userName;
			set => this.RaiseAndSetIfChanged(ref _userName, value);
		}

		/// <summary>
		/// 直播间标题
		/// </summary>
		[JsonIgnore]
		public string? Title
		{
			get => _title;
			set => this.RaiseAndSetIfChanged(ref _title, value);
		}

		/// <summary>
		/// 直播状态
		/// </summary>
		[JsonIgnore]
		public LiveStatus LiveStatus
		{
			get => _liveStatus;
			set => this.RaiseAndSetIfChanged(ref _liveStatus, value);
		}

		/// <summary>
		/// 录制状态
		/// </summary>
		[JsonIgnore]
		public RecordStatus RecordStatus
		{
			get => _recordStatus;
			set => this.RaiseAndSetIfChanged(ref _recordStatus, value);
		}

		/// <summary>
		/// 是否开播提醒
		/// </summary>
		public bool IsNotify
		{
			get => _isNotify;
			set => this.RaiseAndSetIfChanged(ref _isNotify, value);
		}

		/// <summary>
		/// 弹幕重连间隔
		/// 单位 秒
		/// </summary>
		public double DanMuReconnectLatency
		{
			get => _danMuReconnectLatency;
			set => this.RaiseAndSetIfChanged(ref _danMuReconnectLatency, value);
		}

		/// <summary>
		/// Http 开播检查间隔
		/// 单位 秒
		/// </summary>
		public double HttpCheckLatency
		{
			get => _httpCheckLatency;
			set => this.RaiseAndSetIfChanged(ref _httpCheckLatency, value);
		}

		/// <summary>
		/// 直播重连间隔
		/// 单位 秒
		/// </summary>
		public double StreamReconnectLatency
		{
			get => _streamReconnectLatency;
			set => this.RaiseAndSetIfChanged(ref _streamReconnectLatency, value);
		}

		/// <summary>
		/// 直播连接超时
		/// 单位 秒
		/// </summary>
		public double StreamConnectTimeout
		{
			get => _streamConnectTimeout;
			set => this.RaiseAndSetIfChanged(ref _streamConnectTimeout, value);
		}

		/// <summary>
		/// 直播流超时
		/// 单位 秒
		/// </summary>
		public double StreamTimeout
		{
			get => _streamTimeout;
			set => this.RaiseAndSetIfChanged(ref _streamTimeout, value);
		}

		/// <summary>
		/// 速度
		/// </summary>
		[JsonIgnore]
		public string Speed
		{
			get => _speed;
			set => this.RaiseAndSetIfChanged(ref _speed, value);
		}

		/// <summary>
		/// 弹幕服务器类型
		/// </summary>
		public DanmuClientType ClientType
		{
			get => _clientType;
			set => this.RaiseAndSetIfChanged(ref _clientType, value);
		}

		/// <summary>
		/// qn 参数
		/// </summary>
		public Qn Qn
		{
			get => _qn;
			set => this.RaiseAndSetIfChanged(ref _qn, value);
		}

		#endregion

		public RoomStatus()
		{
			_logger = Locator.Current.GetService<ILogger<RoomStatus>>();
			_config = Locator.Current.GetService<Config>();
			_apiClient = Locator.Current.GetService<BililiveApiClient>();
			_taskList = Locator.Current.GetService<TaskListViewModel>();
		}

		#region ApiRequest

		public async Task GetRoomInfoDataAsync(bool isThrow, CancellationToken token)
		{
			try
			{
				var data = await _apiClient.GetRoomInfoDataAsync(RoomId, token);
				CopyFromRoomInfoData(data);
			}
			catch (Exception ex)
			{
				if (!isThrow)
				{
					_logger.LogError(ex, $@"[{RoomId}] 获取房间信息出错");
				}
				else
				{
					throw;
				}
			}
		}

		private async Task GetAnchorInfoAsync(CancellationToken token)
		{
			try
			{
				var info = await _apiClient.GetAnchorInfoDataAsync(RoomId, token);
				UserName = info.uname;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 获取主播信息出错");
			}
		}

		public async Task RefreshStatusAsync(CancellationToken token)
		{
			try
			{
				await GetRoomInfoDataAsync(true, token);
				await GetAnchorInfoAsync(token);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 刷新房间状态出错");
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
			_statusMonitor = this.WhenAnyValue(x => x.LiveStatus).Subscribe(_ => StatusUpdatedAsync().NoWarning());
			_enableMonitor = this.WhenAnyValue(x => x.IsEnable).Subscribe(_ => EnableUpdated());
			this.RaisePropertyChanged(nameof(LiveStatus));
			_titleMonitor = this.WhenAnyValue(x => x.Title).Subscribe(title =>
			{
				if (title is not null)
				{
					_logger.LogInformation($@"[{RoomId}] [TitleChanged] {title}");
				}
			});
			this.RaisePropertyChanged(nameof(Title));
			BuildDanmuClientAsync().NoWarning();
			BuildHttpCheckMonitor();
		}

		private void BuildHttpCheckMonitor()
		{
			_httpMonitor = Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(HttpCheckLatency)).Subscribe(_ => RefreshStatusAsync(default).NoWarning());
		}

		private async ValueTask BuildDanmuClientAsync()
		{
			_danmuClient = ClientType switch
			{
				DanmuClientType.TCP => new TcpDanmuClient(_logger),
				DanmuClientType.Websocket => new WsDanmuClient(_logger),
				_ => new WssDanmuClient(_logger)
			};
			_danmuClient.RetryInterval = TimeSpan.FromSeconds(DanMuReconnectLatency);
			_danmuClient.RoomId = RoomId;
			_danmuClient.ApiClient = _apiClient;

			_danmuClient.Received.Subscribe(ParseDanmu);
			await _danmuClient.StartAsync();
		}

		private async Task StartRecordAsync()
		{
			lock (this)
			{
				if (RecordStatus != RecordStatus.未录制)
				{
					_logger.LogDebug($@"[{RoomId}] 重复录制，已跳过");
					return;
				}
				RecordStatus = RecordStatus.启动中;
				_recordCts = new CancellationTokenSource();
			}

			try
			{
				while (LiveStatus == LiveStatus.直播)
				{
					RecordStatus = RecordStatus.启动中;
					var urlData = await _apiClient.GetPlayUrlDataAsync(RoomId, (long)Qn, _token);
					var url = urlData.durl!.First().url;

					await using var downloader = new HttpDownloader(TimeSpan.FromSeconds(StreamConnectTimeout), _config.Cookie, _config.UserAgent, _config.IsUseProxy)
					{
						Target = new Uri(url!)
					};
					try
					{
						await downloader.GetStreamAsync(_token);
						RecordStatus = RecordStatus.录制中;
						var flv = Path.Combine(_config.MainDir, $@"{RoomId}", $@"{DateTime.Now:yyyyMMdd_HHmmss}.flv");
						downloader.OutFileName = flv;
						_logger.LogInformation($@"[{RoomId}] 开始录制");
						var lastDataReceivedTime = DateTime.Now;
						using var speedMonitor = downloader.CurrentSpeed.Subscribe(b =>
						{
							Speed = $@"{Utils.Utils.CountSize(Convert.ToInt64(b))}/s";
							var now = DateTime.Now;
							if (b > 0.0)
							{
								lastDataReceivedTime = now;
							}
							else if (now - lastDataReceivedTime > TimeSpan.FromSeconds(StreamTimeout))
							{
								// ReSharper disable once AccessToDisposedClosure
								downloader.CloseStream().NoWarning();
								_logger.LogWarning($@"[{RoomId}] 网络不稳定，即将尝试重连");
							}
						});
						try
						{
							await downloader.DownloadAsync(_token);
						}
						finally
						{
							_logger.LogInformation($@"[{RoomId}] 录制结束");
							ConvertToMp4Async(flv).NoWarning();
						}
					}
					catch (OperationCanceledException) { throw; }
					catch (IOException ex) when (ex.InnerException is SocketException { ErrorCode: (int)SocketError.OperationAborted })
					{
						// downloader stream manually closed
					}
					catch (Exception e)
					{
						if (e is HttpRequestException ex)
						{
							_logger.LogInformation($@"[{RoomId}] 尝试下载直播流时服务器返回了 {ex.StatusCode}");
						}
						else
						{
							_logger.LogError(e, $@"[{RoomId}] 尝试下载直播流错误");
						}
						await Task.Delay(TimeSpan.FromSeconds(StreamReconnectLatency), _token);
					}
				}
				_logger.LogInformation($@"[{RoomId}] 不再录制");
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation($@"[{RoomId}] 录制已取消");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 录制出现错误");
			}
			finally
			{
				RecordStatus = RecordStatus.未录制;
				Speed = string.Empty;
			}
		}

		private async Task ConvertToMp4Async(string flv)
		{
			try
			{
				if (_config.IsAutoConvertMp4 && File.Exists(flv))
				{
					var args = string.Format(Utils.Constants.FFmpegCopyConvert, flv, Path.ChangeExtension(flv, @"mp4"));
					var task = new FFmpegTaskViewModel(args);
					await _taskList.AddTaskAsync(task, Path.GetPathRoot(flv) ?? string.Empty);
					_taskList.RemoveTask(task);
					if (_config.IsDeleteAfterConvert)
					{
						File.Delete(flv);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, @"转封装 MP4 时发生错误");
			}
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
			_danmuClient?.DisposeAsync().NoWarning();
			_httpMonitor?.Dispose();
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

				var danMu = DanmuFactory.ParseJson(packet.Body.Span);
				if (danMu is null)
				{
					return;
				}

				var streamingStatus = danMu.IsStreaming();
				if (streamingStatus != LiveStatus.未知)
				{
					LiveStatus = streamingStatus;
				}

				var title = danMu.TitleChanged();
				if (title is not null)
				{
					Title = title;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 弹幕解析失败：{packet.Operation} {packet.ProtocolVersion} {Encoding.UTF8.GetString(packet.Body.Span)}");
			}
		}

		private async ValueTask StatusUpdatedAsync()
		{
			try
			{
				if (LiveStatus != LiveStatus.未知)
				{
					_logger.LogInformation($@"[{RoomId}] 直播状态：{LiveStatus}");
				}

				if (LiveStatus == LiveStatus.直播)
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
				_logger.LogError(ex, $@"[{RoomId}] 启动/停止录制出现错误");
			}
		}

		private void EnableUpdated()
		{
			try
			{
				if (IsEnable)
				{
					if (LiveStatus == LiveStatus.直播)
					{
						StartRecordAsync().NoWarning();
					}
				}
				else
				{
					StopRecord();
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 启动/停止录制出现错误");
			}
		}

		#endregion

		#region Clone

		private void CopyFromRoomInfoData(RoomInfoData roomData)
		{
			RoomId = roomData.room_id;
			ShortId = roomData.short_id;
			LiveStatus = (LiveStatus)roomData.live_status;
			Title = roomData.title;
		}

		public RoomStatus Clone()
		{
			return new()
			{
				IsEnable = IsEnable,
				IsNotify = IsNotify,
				RoomId = RoomId,
				DanMuReconnectLatency = DanMuReconnectLatency,
				HttpCheckLatency = HttpCheckLatency,
				StreamReconnectLatency = StreamReconnectLatency,
				StreamConnectTimeout = StreamConnectTimeout,
				StreamTimeout = StreamTimeout,
				ClientType = ClientType,
				Qn = Qn
			};
		}

		public async ValueTask UpdateAsync(RoomStatus room)
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
				if (_danmuClient is not null)
				{
					await _danmuClient.DisposeAsync();
				}
				await BuildDanmuClientAsync();
			}

			Qn = room.Qn;
		}

		#endregion

		#region Equals

		public override bool Equals(object? obj)
		{
			return obj is RoomStatus room && room.RoomId == RoomId;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(RoomId);
		}

		#endregion
	}
}
