using System.Text.Json.Serialization;

namespace BilibiliLive.Api;

internal sealed record NavData([property: JsonPropertyName("isLogin")] bool IsLogin, long Mid, WbiImg? WbiImg);

internal sealed record WbiImg(string? ImgUrl, string? SubUrl);
