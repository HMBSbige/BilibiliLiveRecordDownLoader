using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.Threading;
using System.Diagnostics;
using WindowsJobAPI;

namespace BilibiliLiveRecordDownLoader.FFmpeg;

public sealed class FFmpegStdInCommand : IDisposable
{
	private const string DefaultFFmpegPath = @"ffmpeg";
	public string FFmpegPath { get; init; } = DefaultFFmpegPath;

	private Process? _process;

	public ILogger Logger { get; set; } = NullLogger.Instance;

	private readonly JobObject _job = new();

	public Stream? InputStream => _process?.StandardInput.BaseStream;

	private async ValueTask ReadErrorAsync(TextReader reader, CancellationToken cancellationToken = default)
	{
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			string? processOutput = await reader.ReadLineAsync(cancellationToken);
			if (processOutput is null)
			{
				break;
			}

			Logger.LogError(@"{message}", processOutput);
		}
	}

	private async ValueTask<bool> VerifyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using Process process = new()
			{
				StartInfo =
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					FileName = FFmpegPath,
					ArgumentList = { @"-version" }
				}
			};
			process.Start();
			_job.AddProcess(process);
			string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
			await process.WaitForExitAsync(cancellationToken);
			return output.StartsWith(@"ffmpeg version");
		}
		catch
		{
			return false;
		}
	}

	public async ValueTask StartAsync(string outputPath, CancellationToken cancellationToken = default)
	{
		if (_process is not null)
		{
			throw new FileLoadException(@"进程已在运行或未释放");
		}
		if (!await VerifyAsync(cancellationToken))
		{
			throw new FileNotFoundException(@"未找到 FFmpeg", FFmpegPath);
		}

		_process = new Process
		{
			StartInfo =
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardError = true,
				FileName = FFmpegPath,
				ArgumentList =
				{
					@"-loglevel",
					@"error",
					@"-i",
					@"-",
					@"-c",
					@"copy",
					@"-y",
					outputPath
				}
			}
		};

		_process.Start();
		_job.AddProcess(_process);

		ReadErrorAsync(_process.StandardError, cancellationToken).Forget();
	}

	public void Stop()
	{
		try
		{
			InputStream?.Dispose();
		}
		catch
		{
			// ignored
		}
	}

	public void Dispose()
	{
		if (_process is null)
		{
			_job.Dispose();
			return;
		}

		Stop();

		_process.WaitForExit(TimeSpan.FromSeconds(5));

		try
		{
			_job.Dispose();
			if (!_process.HasExited)
			{
				_process.Kill();
			}
		}
		catch
		{
			// ignored
		}
		finally
		{
			_process.Dispose();
			_process = null;
		}
	}
}
