using System.Buffers;

namespace MediaToolkit.Flv;

// 第一次有内容要写时才创建文件，这样缺失或不支持的流不会留下空文件
internal sealed class FlvTrack(string path, IFlvTagConverter converter) : IAsyncDisposable
{
	private static readonly FileStreamOptions WriteOptions = new()
	{
		Mode = FileMode.Create,
		Access = FileAccess.Write,
		Share = FileShare.Read,
		Options = FileOptions.Asynchronous
	};

	private readonly ArrayBufferWriter<byte> _buffer = new();
	private FileStream? _output;

	public string? OutputPath => _output is null ? null : path;

	public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		_buffer.ResetWrittenCount();
		converter.Convert(data.Span, _buffer);

		if (_buffer.WrittenCount is 0)
		{
			return;
		}

		_output ??= new FileStream(path, WriteOptions);
		await _output.WriteAsync(_buffer.WrittenMemory, cancellationToken);
	}

	public async ValueTask DiscardAsync()
	{
		if (_output is null)
		{
			return;
		}

		try
		{
			await _output.DisposeAsync();
			File.Delete(path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// 清理失败只会留下残缺文件，不能掩盖导致中止的原始异常
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
