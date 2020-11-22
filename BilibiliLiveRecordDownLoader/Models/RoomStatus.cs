using BilibiliApi.Clients;
using BilibiliApi.Model.RoomInfo;
using BilibiliLiveRecordDownLoader.Enums;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Splat;
using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveRecordDownLoader.Models
{
	public class RoomStatus : ReactiveObject
	{
		private readonly ILogger _logger;
		private readonly Config _config;

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
		private double _speed;
		//TODO 画质选择

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
		public double Speed
		{
			get => _speed;
			set => this.RaiseAndSetIfChanged(ref _speed, value);
		}

		#endregion

		public RoomStatus()
		{
			_logger = Locator.Current.GetService<ILogger<RoomStatus>>();
			_config = Locator.Current.GetService<Config>();
		}

		private void CopyFromRoomInfoData(RoomInfoData roomData)
		{
			RoomId = roomData.room_id;
			ShortId = roomData.short_id;
			LiveStatus = (LiveStatus)roomData.live_status;
			Title = roomData.title;
		}

		private BililiveApiClient CreateClient(TimeSpan timeout)
		{
			return new(timeout, _config.Cookie, _config.UserAgent);
		}

		public async Task GetRoomInfoDataAsync(bool isThrow, CancellationToken token)
		{
			try
			{
				using var client = CreateClient(TimeSpan.FromSeconds(10));
				var data = await client.GetRoomInfoDataAsync(RoomId, token);
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
				using var client = CreateClient(TimeSpan.FromSeconds(10));
				var info = await client.GetAnchorInfoDataAsync(RoomId, token);
				UserName = info.uname;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $@"[{RoomId}] 获取主播信息出错");
			}
		}

		public async Task InitAsync(CancellationToken token)
		{
			if (LiveStatus == LiveStatus.未知)
			{
				await GetRoomInfoDataAsync(false, token);
			}

			if (UserName is null)
			{
				await GetAnchorInfoAsync(token);
			}
		}
	}
}
