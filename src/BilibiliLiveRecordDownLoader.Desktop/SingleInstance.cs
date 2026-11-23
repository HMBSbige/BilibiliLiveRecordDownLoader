using System.Buffers;
using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Desktop;

internal sealed partial class SingleInstance : IDisposable
{
	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private readonly string _name;
	private readonly Mutex _mutex;
	private readonly CancellationTokenSource _cancellation = new();
	private bool _isOwned;

	public SingleInstance(string id)
	{
		// 用哈希缩短名称，Unix 域套接字路径有长度限制
		// 命名管道不按会话隔离，名称不带会话时后启动的会话建不了管道
		string scope = OperatingSystem.IsWindows() ? $"{id}\0{Environment.UserName}\0{GetSessionId()}" : $"{id}\0{Environment.UserName}";
		byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(scope.Length));
		Span<byte> hash = stackalloc byte[PlatformSha256.HashLength];

		try
		{
			ReadOnlySpan<byte> utf8 = buffer.AsSpan(0, Encoding.UTF8.GetBytes(scope, buffer));

			if (PlatformSha256.IsPreferredFor(utf8.Length))
			{
				PlatformSha256.HashData(utf8, hash);
			}
			else
			{
				HashAlgorithm<Sha256Core>.HashData(utf8, hash);
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		_name = Convert.ToHexStringLower(hash.Slice(0, 12));

		// Unix 上的“会话”是进程会话，不同终端启动的进程互不相同
		_mutex = new Mutex(false, _name, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = OperatingSystem.IsWindows() });
	}

	public bool TryAcquire(TimeSpan timeout)
	{
		try
		{
			_isOwned = _mutex.WaitOne(timeout);
		}
		catch (AbandonedMutexException)
		{
			_isOwned = true;
		}

		return _isOwned;
	}

	public bool ActivateFirstInstance()
	{
		try
		{
			using NamedPipeClientStream client = new(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
			client.Connect(ConnectTimeout);

			if (OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(client.SafePipeHandle, out uint processId))
			{
				AllowSetForegroundWindow(processId);
			}

			client.WriteByte(1);
			return true;
		}
		catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	public void StartListening(Action onActivated)
	{
		CancellationToken cancellationToken = _cancellation.Token;
		_ = Task.Run(() => ListenAsync(onActivated, cancellationToken), cancellationToken);
	}

	public void StopListening()
	{
		_cancellation.Cancel();
	}

	private async Task ListenAsync(Action onActivated, CancellationToken cancellationToken)
	{
		byte[] request = new byte[1];

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await using NamedPipeServerStream server = new(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
				await server.WaitForConnectionAsync(cancellationToken);

				// 读完请求再关闭，否则客户端写入时会遇到已断开的管道
				if (await server.ReadAsync(request, cancellationToken) > 0)
				{
					onActivated();
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}
	}

	private static int GetSessionId()
	{
		using Process process = Process.GetCurrentProcess();
		return process.SessionId;
	}

	[LibraryImport("kernel32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	[SupportedOSPlatform("windows")]
	private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

	[LibraryImport("user32.dll")]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	[SupportedOSPlatform("windows")]
	private static partial bool AllowSetForegroundWindow(uint processId);

	public void Dispose()
	{
		_cancellation.Cancel();
		_cancellation.Dispose();

		if (_isOwned)
		{
			_mutex.ReleaseMutex();
		}

		_mutex.Dispose();
	}
}
