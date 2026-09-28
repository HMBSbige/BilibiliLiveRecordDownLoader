using BilibiliLive.Danmu;
using System.Text.Json.Serialization;

namespace BilibiliLive.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(RoomBaseInfoData))]
[JsonSerializable(typeof(RoomPlayInfoData))]
[JsonSerializable(typeof(DanmuInfoData))]
[JsonSerializable(typeof(NavData))]
[JsonSerializable(typeof(QrCodeGenerateData))]
[JsonSerializable(typeof(QrCodePollData))]
[JsonSerializable(typeof(DanmuAuthBody))]
internal sealed partial class BilibiliJsonContext : JsonSerializerContext;
