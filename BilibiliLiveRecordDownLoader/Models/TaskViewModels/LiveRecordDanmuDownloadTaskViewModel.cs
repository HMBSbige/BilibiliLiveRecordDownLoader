using BilibiliApi.Clients;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace BilibiliLiveRecordDownLoader.Models.TaskViewModels
{
	public class LiveRecordDanmuDownloadTaskViewModel : TaskViewModel
	{
		private readonly ILogger _logger;
		private readonly BilibiliApiClient _apiClient;

		private readonly CancellationTokenSource _cts = new();
		private readonly LiveRecordViewModel _liveRecord;
		private readonly string _path;
		private readonly string _dir;

		private static readonly XmlWriterSettings XmlWriterSettings = new()
		{
			Async = true,
			Indent = true,
			IndentChars = "\t",
			CloseOutput = true
		};

		public LiveRecordDanmuDownloadTaskViewModel(
			ILogger logger,
			BilibiliApiClient apiClient,
			LiveRecordViewModel liveRecord, string path)
		{
			_logger = logger;
			_apiClient = apiClient;

			_liveRecord = liveRecord;
			_dir = path;
			_path = Path.Combine(path, $@"{_liveRecord.StartTime:yyyyMMdd_HHmmss}_{_liveRecord.Title}.xml".RemoveInvalidFileNameChars());

			Description = $@"{liveRecord.Rid} 弹幕";
		}

		public override async Task StartAsync()
		{
			try
			{
				_cts.Token.ThrowIfCancellationRequested();

				Status = @"正在获取回放弹幕信息...";
				var rid = _liveRecord.Rid!;
				var danmuInfo = await _apiClient.GetDanmuInfoByLiveRecordAsync(rid, _cts.Token);

				if (danmuInfo is null || danmuInfo.code != 0 || danmuInfo.data?.dm_info is null)
				{
					throw new Exception($@"获取回放弹幕信息失败 {danmuInfo?.message}");
				}

				var totalIndex = danmuInfo.data.dm_info.num;
				if (totalIndex <= 0)
				{
					Status = @"无法获取弹幕页数，可能无弹幕";
					Progress = 1.0;
					return;
				}

				Status = @"开始下载弹幕...";
				Progress = 0.0;

				Directory.CreateDirectory(_dir);
				await using var writer = XmlWriter.Create(File.Create(_path), XmlWriterSettings);
				await writer.WriteStartDocumentAsync();

				await writer.WriteStartElementAsync(null, @"i", null);
				await writer.WriteCommentAsync(rid.EscapeXmlChars());
				await writer.WriteElementStringAsync(null, @"chatserver", null, @"chat.bilibili.com");
				await writer.WriteElementStringAsync(null, @"chatid", null, @"0");
				await writer.WriteElementStringAsync(null, @"mission", null, @"0");
				await writer.WriteElementStringAsync(null, @"maxlimit", null, @"0");
				await writer.WriteElementStringAsync(null, @"state", null, @"0");
				await writer.WriteElementStringAsync(null, @"real_name", null, @"0");
				await writer.WriteElementStringAsync(null, @"source", null, @"k-v");

				for (var i = 0L; i < totalIndex; ++i)
				{
					var danmuMsgInfo = await _apiClient.GetDmMsgByPlayBackIdAsync(rid, i, _cts.Token);
					if (danmuMsgInfo is null || danmuMsgInfo.code != 0)
					{
						throw new Exception($@"[{i + 1}/{totalIndex}] 获取回放弹幕失败 {danmuMsgInfo?.message}");
					}
					var info = danmuMsgInfo.data?.dm?.dm_info;

					if (info is null)
					{
						continue;
					}

					foreach (var dm in info)
					{
						var ts = TimeSpan.FromMilliseconds((ulong)dm.ts).TotalSeconds;
						var type = dm.dm_mode;
						var size = dm.dm_fontsize;
						var color = dm.dm_color;
						var timestamp = (ulong)(dm.check_info?.ts ?? 0) / 1000;
						const ulong pool = 0;
						var uid = dm.uid; // 应该是 crc32，但是我们直接记录 uid 又有什么问题呢？
						const ulong rowId = 0;

						await writer.WriteStartElementAsync(null, @"d", null);
						await writer.WriteAttributeStringAsync(null, @"p", null,
							$@"{ts},{type},{size},{color},{timestamp},{pool},{uid},{rowId}");
						await writer.WriteStringAsync(dm.text.EscapeXmlChars());
						await writer.WriteEndElementAsync();
					}

					Status = $@"[{i + 1}/{totalIndex}] 正在下载弹幕...";
					Progress = (i + 1) / (double)totalIndex;
				}

				Status = @"完成";
				Progress = 1.0;
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation($@"下载已取消：{_liveRecord.Rid}");
			}
			catch (Exception ex)
			{
				Status = @"出错";
				_logger.LogError(ex, @"下载直播回放弹幕出错");
			}
			finally
			{
				Speed = string.Empty;
			}
		}

		public override void Stop()
		{
			_cts.Cancel();
		}
	}
}
