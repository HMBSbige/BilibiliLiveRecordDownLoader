using System.Buffers;

namespace BilibiliLive.Streams;

internal sealed class HttpStreamSession : LiveStreamSession
{
	private const int BufferSize = 64 * 1024;

	private readonly HttpResponseMessage _response;

	private HttpStreamSession(Uri source, HttpResponseMessage response)
	{
		Source = source;
		_response = response;
	}

	public override Uri Source { get; }

	public static async Task<LiveStreamSession> OpenAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, uri);
		HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

		try
		{
			response.EnsureSuccessStatusCode();
			return new HttpStreamSession(uri, response);
		}
		catch
		{
			response.Dispose();
			throw;
		}
	}

	public override async Task CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		await using Stream source = await _response.Content.ReadAsStreamAsync(cancellationToken);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

		try
		{
			while (true)
			{
				int read = await source.ReadAsync(buffer, cancellationToken);

				if (read is 0)
				{
					return;
				}

				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				progress?.Report(read);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public override ValueTask DisposeAsync()
	{
		_response.Dispose();
		return ValueTask.CompletedTask;
	}
}
