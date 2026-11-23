using BilibiliLive.Enums;
using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Configuration;

// 3.x 的 WPF 版：属性名和类里的一样，枚举写成数字，等于默认值的不写；没写的项两边默认值相同
internal static class LegacyConfig
{
	public static string GetBackupPath(string path)
	{
		return Path.ChangeExtension(path, ".backup.json");
	}

	// 类型不对的值留给读取时丢掉
	public static JsonObject ToCurrent(JsonObject legacy)
	{
		JsonObject config = [];

		// 旧版没写录制目录时录到“视频”文件夹本身
		config[nameof(AppConfig.RecordDirectory)] = legacy["MainDir"]?.DeepClone() ?? JsonValue.Create(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
		Copy(legacy, "IsCheckUpdateOnStart", config, nameof(AppConfig.AutoCheckUpdate));
		Copy(legacy, "IsCheckPreRelease", config, nameof(AppConfig.IncludePreRelease));
		Copy(legacy, "MainWindowsWidth", config, nameof(AppConfig.WindowWidth));
		Copy(legacy, "MainWindowsHeight", config, nameof(AppConfig.WindowHeight));
		Copy(legacy, "UserAgent", config, nameof(AppConfig.UserAgent));
		Copy(legacy, "Cookie", config, nameof(AppConfig.Cookie));
		Copy(legacy, "IsUseProxy", config, nameof(AppConfig.UseSystemProxy));
		Copy(legacy, "Theme", config, nameof(AppConfig.Theme));
		Copy(legacy, "StreamHostRule", config, nameof(AppConfig.StreamHostSelection));
		Copy(legacy, "IsAutoConvertMp4", config, nameof(AppConfig.ConvertToMp4));
		Copy(legacy, "IsDeleteAfterConvert", config, nameof(AppConfig.DeleteAfterConvert));
		Set(config, nameof(AppConfig.RecorderType), GetRecorderType(legacy));
		Set(config, nameof(AppConfig.CodecPriority), GetPriority<StreamCodec>(legacy, "AutoRecordCodecOrder"));
		Set(config, nameof(AppConfig.FormatPriority), GetPriority<StreamFormat>(legacy, "AutoRecordFormatOrder"));

		if (legacy["Rooms"] is JsonArray rooms)
		{
			config[nameof(AppConfig.Rooms)] = new JsonArray([.. rooms.OfType<JsonObject>().Select(ToCurrentRoom)]);
		}

		return config;
	}

	private static JsonObject ToCurrentRoom(JsonObject legacy)
	{
		JsonObject room = [];

		Copy(legacy, "RoomId", room, nameof(RoomConfig.RoomId));
		Copy(legacy, "IsEnable", room, nameof(RoomConfig.IsRecordingEnabled));
		Copy(legacy, "IsNotify", room, nameof(RoomConfig.IsNotificationEnabled));
		Copy(legacy, "IsAutoConvertMp4", room, nameof(RoomConfig.ConvertToMp4));
		Copy(legacy, "IsDeleteAfterConvert", room, nameof(RoomConfig.DeleteAfterConvert));
		CopySeconds(legacy, "HttpCheckLatency", room, nameof(RoomConfig.StatusCheckInterval));
		CopySeconds(legacy, "DanMuReconnectLatency", room, nameof(RoomConfig.DanmuReconnectDelay));
		CopySeconds(legacy, "StreamReconnectLatency", room, nameof(RoomConfig.StreamReconnectDelay));
		CopySeconds(legacy, "StreamConnectTimeout", room, nameof(RoomConfig.StreamConnectTimeout));
		CopySeconds(legacy, "StreamTimeout", room, nameof(RoomConfig.StreamIdleTimeout));
		Set(room, nameof(RoomConfig.RecorderType), GetRecorderType(legacy));
		Set(room, nameof(RoomConfig.CodecPriority), GetPriority<StreamCodec>(legacy, "AutoRecordCodecOrder"));
		Set(room, nameof(RoomConfig.FormatPriority), GetPriority<StreamFormat>(legacy, "AutoRecordFormatOrder"));

		// 画质会原样拿去请求，加载时不检查
		if (legacy["Qn"] is JsonValue qn && qn.TryGetValue(out long quality) && Enum.IsDefined((StreamQuality)quality))
		{
			room[nameof(RoomConfig.Quality)] = JsonValue.Create(quality);
		}

		return room;
	}

	private static void Copy(JsonObject from, string fromName, JsonObject to, string toName)
	{
		Set(to, toName, from[fromName]?.DeepClone());
	}

	// 写 null 会被当成无效值丢掉并记警告
	private static void Set(JsonObject to, string name, JsonNode? value)
	{
		if (value is not null)
		{
			to[name] = value;
		}
	}

	private static void CopySeconds(JsonObject from, string fromName, JsonObject to, string toName)
	{
		if (from[fromName] is JsonValue value && value.TryGetValue(out double seconds) && double.IsFinite(seconds))
		{
			// 加载时还会按设置的范围限制，这里只防转换溢出
			to[toName] = JsonValue.Create(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, (double)RoomDefaults.MaxIntervalSeconds)).ToString("c", CultureInfo.InvariantCulture));
		}
	}

	// 旧版 0 是跟随全局设置（全局时等于内置录制），1 是内置录制，3 是 FFmpeg
	private static JsonValue? GetRecorderType(JsonObject legacy)
	{
		if (legacy["RecorderType"] is not JsonValue value || !value.TryGetValue(out long type))
		{
			return null;
		}

		return type switch
		{
			1 => JsonValue.Create(nameof(RecorderType.BuiltIn)),
			3 => JsonValue.Create(nameof(RecorderType.FFmpeg)),
			_ => null
		};
	}

	// 分号分隔、排在前面的优先；认不出的名字跳过，一个都认不出时和没写一样
	private static JsonArray? GetPriority<T>(JsonObject legacy, string name) where T : struct, Enum
	{
		if (legacy[name] is not JsonValue value || !value.TryGetValue(out string? order))
		{
			return null;
		}

		string[] names = Enum.GetNames<T>();
		JsonArray priority = [];

		foreach (string item in order.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (Array.Find(names, candidate => candidate.Equals(item, StringComparison.OrdinalIgnoreCase)) is { } matched)
			{
				// 泛型的 Add 不能裁剪
				priority.Add((JsonNode?)JsonValue.Create(matched));
			}
		}

		return priority.Count > 0 ? priority : null;
	}
}
