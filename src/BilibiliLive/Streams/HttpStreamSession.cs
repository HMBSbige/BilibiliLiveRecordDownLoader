using System.Buffers;

namespace BilibiliLive.Streams;

// B 站的 http_stream 只提供 FLV
internal sealed class HttpStreamSession : LiveStreamSession
{
	private const int BufferSize = 64 * 1024;

	private readonly HttpResponseMessage _response;
	private readonly Stream _body;
	private readonly byte[] _header;

	private HttpStreamSession(Uri source, HttpResponseMessage response, Stream body, byte[] header)
	{
		Source = source;
		_response = response;
		_body = body;
		_header = header;
	}

	public override Uri Source { get; }

	public static async Task<LiveStreamSession> OpenAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

		try
		{
			response.EnsureSuccessStatusCode();
			Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

			// 单凭 200 说明不了什么：出故障的节点仍可能返回错误页面或者立刻断开
			byte[] header = new byte[MediaProbe.FlvSignature.Length];
			int read = await body.ReadAtLeastAsync(header, header.Length, false, cancellationToken);

			if (!MediaProbe.IsFlv(header.AsSpan(0, read)))
			{
				throw new InvalidDataException(read is 0 ? "The stream ended before sending any data." : "The stream does not start with an FLV header.");
			}

			return new HttpStreamSession(uri, response, body, header);
		}
		catch
		{
			response.Dispose();
			throw;
		}
	}

	public override async Task<StreamEndReason> CopyToAsync(Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		await destination.WriteAsync(_header, cancellationToken);
		progress?.Report(_header.Length);

		byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

		try
		{
			while (await _body.ReadAsync(buffer, cancellationToken) is var read and > 0)
			{
				await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				progress?.Report(read);
			}

			return StreamEndReason.EndOfStream;
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await _body.DisposeAsync();
		_response.Dispose();
	}
}
