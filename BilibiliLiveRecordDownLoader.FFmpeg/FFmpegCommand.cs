using Microsoft.VisualStudio.Threading;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WindowsJobAPI;

namespace BilibiliLiveRecordDownLoader.FFmpeg;

public sealed class FFmpegCommand : IDisposable
{
	private const string DefaultFFmpegPath = @"ffmpeg";

	private Process? _process;
	private readonly JobObject _job = new();

	public string FFmpegPath { get; init; } = DefaultFFmpegPath;

	private readonly Subject<string> _messageUpdated = new();
	public IObservable<string> MessageUpdated => _messageUpdated.AsObservable();

	private static Process CreateProcess(string path, string args)
	{
		return new Process
		{
			StartInfo =
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardInput = true,
				RedirectStandardOutput = false,
				RedirectStandardError = false,
				FileName = path,
				Arguments = args
			}
		};
	}

	private async Task ReadOutputAsync(TextReader reader, CancellationToken cancellationToken = default)
	{
		string? lastMessage = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			string? processOutput = await reader.ReadLineAsync(cancellationToken);
			if (processOutput is null)
			{
				break;
			}

			lastMessage = processOutput;
			_messageUpdated.OnNext(lastMessage);
		}
		_messageUpdated.OnNext($@"[已完成]{lastMessage}");
	}

	public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using Process process = CreateProcess(FFmpegPath, @"-version");
			process.StartInfo.RedirectStandardOutput = true;
			process.Start();
			_job.AddProcess(process);
			string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
			await process.WaitForExitAsync(cancellationToken);

			const string versionString = @"ffmpeg version";
			if (!output.StartsWith(versionString))
			{
				return null;
			}
			string[] ss = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			return ss.Length < 3 ? @"Unknown" : ss[2];
		}
		catch
		{
			return null;
		}
	}

	public async Task<bool> VerifyAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using Process process = CreateProcess(FFmpegPath, @"-version");
			process.StartInfo.RedirectStandardOutput = true;
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

	public async Task StartAsync(string args, CancellationToken cancellationToken = default)
	{
		try
		{
			if (_process is not null)
			{
				throw new FileLoadException(@"进程已在运行或未释放");
			}
			if (!await VerifyAsync(cancellationToken))
			{
				throw new FileNotFoundException(@"未找到 FFmpeg", FFmpegPath);
			}
			_process = CreateProcess(FFmpegPath, args);
			_process.StartInfo.RedirectStandardOutput = true;
			_process.StartInfo.RedirectStandardError = true;

			_process.Start();
			_job.AddProcess(_process);

			ReadOutputAsync(_process.StandardOutput, cancellationToken).Forget();
			ReadOutputAsync(_process.StandardError, cancellationToken).Forget();

			await _process.WaitForExitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			Stop();
			throw;
		}
	}

	public void Stop()
	{
		try
		{
			if (_process is null)
			{
				return;
			}

			_process.StandardInput.Write('q');
		}
		catch
		{
			// ignored
		}
	}

	public void Dispose()
	{
		_messageUpdated.OnCompleted();
		_job.Dispose();

		Stop();

		if (_process is null)
		{
			return;
		}

		_process.WaitForExit(TimeSpan.FromSeconds(5));

		try
		{
			// Win7 Job API 有问题，可能没正常退出，需要手动杀一下
			// Win7 爬
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
