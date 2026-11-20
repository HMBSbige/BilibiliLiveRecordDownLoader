namespace BilibiliLiveRecordDownLoader.Desktop;

internal sealed class SingleInstance : IDisposable
{
	private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

	private readonly string _name;
	private readonly Mutex _mutex;
	private readonly CancellationTokenSource _cancellation = new();
	private bool _isOwned;

	public SingleInstance(string id)
	{
		// 按用户隔离；用哈希缩短名称，Unix 域套接字路径有长度限制
		_name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}\0{Environment.UserName}")).AsSpan(0, 12));

		// Unix 上的“会话”是进程会话，不同终端启动的进程互不相同，所以不按会话隔离
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
			// 上一个实例异常退出，所有权已转给当前进程
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
			client.WriteByte(1);
			return true;
		}
		catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
		{
			// 首个实例可能正在启动或退出
			return false;
		}
	}

	public void StartListening(Action onActivated)
	{
		CancellationToken cancellationToken = _cancellation.Token;
		_ = Task.Run(() => ListenAsync(onActivated, cancellationToken), cancellationToken);
	}

	// 退出过程中不再接受激活，新实例会等待互斥量释放后自行启动
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
			catch (IOException)
			{
				await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			}
		}
	}

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
