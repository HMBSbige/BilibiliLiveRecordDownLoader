using BilibiliApi.Enums;
using BilibiliApi.Model;
using BilibiliApi.Model.Danmu;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BilibiliLiveDanmuPreviewer;

[UsedImplicitly]
public class MainService : ServiceBase
{
	public async ValueTask DoAsync(CancellationToken cancellationToken = default)
	{
		long[]? roomList = Configuration.GetSection(@"RoomList").Get<long[]>();
		Verify.Operation(roomList is not null && roomList.Any(), @"无房间号");

		List<Task> list = new(roomList.Length);

		foreach (long roomId in roomList)
		{
			BilibiliApiClient apiClient = ServiceProvider.GetRequiredService<BilibiliApiClient>();
			LiveRoomInfo roomInfo = await apiClient.GetLiveRoomInfoAsync(roomId, cancellationToken);

			long realId = roomInfo.RoomId;
			Assumes.False(realId is 0);

			Task task = Task.Run(async () =>
				{
					IDanmuClient client = ServiceProvider.GetRequiredService<IDanmuClient>();

					cancellationToken.Register(() => client.Dispose());

					client.RoomId = realId;
					client.Received.Subscribe(ParseDanmu);

					await client.StartAsync();
				},
				cancellationToken);
			list.Add(task);
		}

		await Task.WhenAll(list);

		return;

		void ParseDanmu(DanmuPacket packet)
		{
			try
			{
				switch (packet.Operation)
				{
					case Operation.HeartbeatReply:
					{
						SequenceReader<byte> reader = new(packet.Body);
						reader.TryReadBigEndian(out int num);
						Logger.LogDebug(@"收到弹幕 [{operation}] 人气值: {number}", packet.Operation, num);
						break;
					}
					case Operation.SendMsgReply:
					{
						string json = Encoding.UTF8.GetString(packet.Body);
						ParseSendMsgReplyBody(json);
						break;
					}
					case Operation.AuthReply:
					{
						Logger.LogDebug(@"收到弹幕 [{operation}]: {body}", packet.Operation, Encoding.UTF8.GetString(packet.Body));
						break;
					}
					default:
					{
						Logger.LogDebug(@"收到弹幕 [{operation}]", packet.Operation);
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, @"弹幕解析失败：{operation} {protocolVersion} {body}", packet.Operation, packet.ProtocolVersion, Encoding.UTF8.GetString(packet.Body));
			}

			return;

			void ParseSendMsgReplyBody(string json)
			{
				if (Configuration.GetSection(@"IsLogJson").Get<bool>())
				{
					Logger.LogDebug(@"收到弹幕 [{operation}]: {body}", packet.Operation, json);
				}

				using JsonDocument document = JsonDocument.Parse(json);
				JsonElement root = document.RootElement;

				string? cmd = root.GetProperty(@"cmd").GetString();

				if (cmd is null)
				{
					return;
				}

				HashSet<string>? ignoreCmd = Configuration.GetSection(@"IgnoreCmd").Get<HashSet<string>>();

				if (ignoreCmd is not null && ignoreCmd.Contains(cmd))
				{
					return;
				}

				switch (cmd)
				{
					case @"COMMON_NOTICE_DANMAKU":
					{
						DefaultInterpolatedStringHandler handler = new();

						foreach (JsonElement segments in root.GetProperty(@"data").GetProperty(@"content_segments").EnumerateArray())
						{
							string? text = segments.GetProperty(@"text").GetString();

							if (text is not null)
							{
								handler.AppendLiteral(text);
							}
						}

						Logger.LogInformation(@"[{cmd}] {notice}", cmd, handler.ToStringAndClear());
						break;
					}
					case @"NOTICE_MSG":
					{
						string? msg = root.GetProperty(@"msg_self").GetString();

						Logger.LogInformation(@"[{cmd}] {notice}", cmd, msg);
						break;
					}
					case @"LIVE":
					{
						Logger.LogInformation(@"{action}", @"开播");
						break;
					}
					case @"PREPARING":
					{
						if (root.TryGetProperty(@"round", out JsonElement element) && element.TryGetInt64(out long round) && round is 1)
						{
							Logger.LogInformation(@"{action}", @"轮播");
						}
						else
						{
							Logger.LogInformation(@"{action}", @"下播");
						}

						break;
					}
					case @"ROOM_CHANGE":
					{
						JsonElement data = root.GetProperty(@"data");
						string? title = data.GetProperty(@"title").GetString();
						string? areaName = data.GetProperty(@"area_name").GetString();
						string? parentAreaName = data.GetProperty(@"parent_area_name").GetString();

						Logger.LogInformation(@"[{cmd}] 标题：{title} 分区：{parentArea}·{area}", cmd, title, parentAreaName, areaName);
						break;
					}
					case @"WATCHED_CHANGE":
					{
						Logger.LogInformation(@"{roomId} 人看过", root.GetProperty(@"data").GetProperty(@"num").GetInt64());
						break;
					}
					case @"WARNING":
					{
						Logger.LogInformation(@"超管警告：{message}", root.GetProperty(@"msg").GetString());

						break;
					}
					case @"CUT_OFF":
					{
						Logger.LogInformation(@"直播被切断：{message}", root.GetProperty(@"msg").GetString());

						break;
					}
					case @"INTERACT_WORD":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"uname").GetString();
						ulong uid = data.GetProperty(@"uid").GetUInt64();
						InteractiveType type = (InteractiveType)data.GetProperty(@"msg_type").GetInt32();

						Logger.LogInformation(@"{user}({uid}){action}了直播间", userName, uid, type);
						break;
					}
					case @"ENTRY_EFFECT":
					{
						JsonElement data = root.GetProperty(@"data");
						string? msg = data.GetProperty(@"copy_writing").GetString();
						PrivilegeType type = (PrivilegeType)data.GetProperty(@"privilege_type").GetInt32();

						if (msg is not null && Enum.IsDefined(type))
						{
							int start = msg.IndexOf(@"<%", StringComparison.Ordinal);
							msg = msg.Remove(start, 2);

							int end = msg.LastIndexOf(@"%>", StringComparison.Ordinal);
							msg = msg.Remove(end, 2);

							Logger.LogInformation(@"{msg}", msg);
						}

						break;
					}
					case @"DANMU_MSG":
					{
						JsonElement info = root.GetProperty(@"info");
						string? msg = info[1].GetString();
						string? userName = info[2][1].GetString();

						Logger.LogInformation(@"{user}：{msg}", userName, msg);
						break;
					}
					case @"LIKE_INFO_V3_UPDATE":
					{
						JsonElement data = root.GetProperty(@"data");
						Logger.LogInformation(@"点赞数: {count}", data.GetProperty(@"click_count").GetInt64());
						break;
					}
					case @"LIKE_INFO_V3_CLICK":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"uname").GetString();
						string? msg = data.GetProperty(@"like_text").GetString();

						Logger.LogInformation(@"{user} {text}", userName, msg);
						break;
					}
					case @"SEND_GIFT":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"uname").GetString();
						string? action = data.GetProperty(@"action").GetString();
						string? giftName = data.GetProperty(@"giftName").GetString();
						long num = data.GetProperty(@"num").GetInt64();

						Logger.LogInformation(@"{user} {action} {giftName}x{num}", userName, action, giftName, num);
						break;
					}
					case @"COMBO_SEND":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"uname").GetString();
						string? action = data.GetProperty(@"action").GetString();
						string? giftName = data.GetProperty(@"gift_name").GetString();
						long num = data.GetProperty(@"total_num").GetInt64();

						Logger.LogInformation(@"{user} {action} {giftName}x{num}", userName, action, giftName, num);

						break;
					}
					case @"GUARD_BUY":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"username").GetString();
						string? giftName = data.GetProperty(@"gift_name").GetString();
						long num = data.GetProperty(@"num").GetInt64();

						Logger.LogInformation(@"{user} 购买了 {giftName}x{num}", userName, giftName, num);
						break;
					}
					case @"SUPER_CHAT_MESSAGE":
					{
						JsonElement data = root.GetProperty(@"data");
						string? userName = data.GetProperty(@"user_info").GetProperty(@"uname").GetString();
						string? giftName = data.GetProperty(@"gift").GetProperty(@"gift_name").GetString();
						long num = data.GetProperty(@"gift").GetProperty(@"num").GetInt64();
						string? message = data.GetProperty(@"message").GetString();
						long price = data.GetProperty(@"price").GetInt64();

						Logger.LogInformation(@"{user} 购买了 {price}元{giftName}x{num}：{message}", userName, price, giftName, num, message);
						break;
					}
					default:
					{
						if (Configuration.GetSection(@"IsLogUnresolvedCmd").Get<bool>())
						{
							Logger.LogInformation(@"收到弹幕 [{operation}]: {body}", packet.Operation, json);
						}

						break;
					}
				}
			}
		}
	}
}
