using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace BilibiliLive.Api;

internal sealed record ApiResponse<T>(long Code, string Message, T? Data) where T : class
{
	public T GetData()
	{
		if (Code is not 0)
		{
			throw new BilibiliApiException(Message, Code);
		}

		return Data ?? throw new BilibiliApiException("The response contains no data.", Code);
	}
}

internal static class ApiResponse
{
	public static async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken) where T : class
	{
		response.EnsureSuccessStatusCode();

		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, default, cancellationToken);
		JsonElement root = document.RootElement;

		if (ReadEnvelope(root) is not (var code, var message))
		{
			throw new BilibiliApiException("The response is not a Bilibili API envelope.");
		}

		// 出错时 data 的形状不定（null、[]、{}），但 nav 返回 -101 时仍带着 wbi 密钥
		T? data = root.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind is JsonValueKind.Object ? dataElement.Deserialize(typeInfo) : null;

		return new ApiResponse<T>(code, message, data);
	}

	// 不返回信封的接口出错时仍会返回信封，靠其中的 code 区分风控和错误请求
	public static void ThrowIfError(string content)
	{
		if (!content.AsSpan().TrimStart().StartsWith('{'))
		{
			return;
		}

		using JsonDocument document = JsonDocument.Parse(content);

		if (ReadEnvelope(document.RootElement) is (not 0 and var code, var message))
		{
			throw new BilibiliApiException(message, code);
		}
	}

	private static (long Code, string Message)? ReadEnvelope(JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("code", out JsonElement codeElement) || codeElement.ValueKind is not JsonValueKind.Number || !codeElement.TryGetInt64(out long code))
		{
			return null;
		}

		return (code, GetMessage(root) ?? string.Create(CultureInfo.InvariantCulture, $"Bilibili API returned code {code}."));
	}

	private static string? GetMessage(JsonElement root)
	{
		foreach (string name in (ReadOnlySpan<string>)["message", "msg"])
		{
			if (root.TryGetProperty(name, out JsonElement element) && element.ValueKind is JsonValueKind.String && element.GetString() is { Length: > 0 } message)
			{
				return message;
			}
		}

		return null;
	}
}
