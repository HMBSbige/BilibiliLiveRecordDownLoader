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

		// Error responses carry data of arbitrary shape (null, [], {}), but nav still returns the wbi keys alongside -101
		T? data = root.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind is JsonValueKind.Object ? dataElement.Deserialize(typeInfo) : null;

		return new ApiResponse<T>(code, message, data);
	}

	public static (long Code, string Message)? ReadEnvelope(JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("code", out JsonElement codeElement) || codeElement.ValueKind is not JsonValueKind.Number || !codeElement.TryGetInt64(out long code))
		{
			return null;
		}

		return (code, GetMessage(root) ?? $"Bilibili API returned code {code}.");
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
