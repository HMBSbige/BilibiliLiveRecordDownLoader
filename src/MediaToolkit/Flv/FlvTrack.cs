using System.Buffers;

namespace MediaToolkit.Flv;

internal sealed class FlvTrack(string path, IFlvTagConverter converter) : IAsyncDisposable
{
	private static readonly FileStreamOptions WriteOptions = new()
	{
		Mode = FileMode.Create,
		Access = FileAccess.Write,
		Share = FileShare.Read,
		Options = FileOptions.Asynchronous,
		BufferSize = 1 << 16
	};

	private readonly ArrayBufferWriter<byte> _buffer = new();
	private FileStream? _output;

	public string? OutputPath => _output is null ? null : path;

	// 参数集攒到下一帧再写：只有参数集的流解码不出画面，不能算作有这条流
	public async ValueTask<FlvTagConversion> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		FlvTagConversion conversion = converter.Convert(data.Span, _buffer);

		if (conversion is FlvTagConversion.Frame)
		{
			_output ??= new FileStream(path, WriteOptions);
			await _output.WriteAsync(_buffer.WrittenMemory, cancellationToken);
			_buffer.ResetWrittenCount();
		}

		return conversion;
	}

	public async ValueTask DiscardAsync()
	{
		if (_output is null)
		{
			return;
		}

		try
		{
			try
			{
				await _output.DisposeAsync();
			}
			finally
			{
				// 刷写失败时句柄照样关闭，文件仍要删
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_output is not null)
		{
			await _output.DisposeAsync();
		}
	}
}
