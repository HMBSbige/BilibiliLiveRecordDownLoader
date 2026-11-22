using System.Buffers;

namespace MediaToolkit.Flv;

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

	public bool HasUnsupportedInput { get; private set; }

	/// <returns>写出了一个编码帧</returns>
	public async ValueTask<bool> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
	{
		if (data.IsEmpty)
		{
			return false;
		}

		_buffer.ResetWrittenCount();
		FlvTagConversion conversion = converter.Convert(data.Span, _buffer);

		if (conversion is FlvTagConversion.Unsupported)
		{
			HasUnsupportedInput = true;
			return false;
		}

		if (_buffer.WrittenCount is 0)
		{
			return false;
		}

		_output ??= new FileStream(path, WriteOptions);
		await _output.WriteAsync(_buffer.WrittenMemory, cancellationToken);
		return conversion is FlvTagConversion.Frame;
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
