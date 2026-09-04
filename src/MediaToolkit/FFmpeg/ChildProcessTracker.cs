using System.Runtime.Versioning;

namespace MediaToolkit.FFmpeg;

// 保证子进程不会在本进程退出后残留：Windows 交给 Job Object，其他平台在 ProcessExit 时逐个结束
internal static class ChildProcessTracker
{
	private static readonly Lock Gate = new();
	private static readonly HashSet<Process> Running = [];

	static ChildProcessTracker()
	{
		if (!OperatingSystem.IsWindows())
		{
			AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
		}
	}

	public static Process Start(ProcessStartInfo startInfo)
	{
		Process process = new() { StartInfo = startInfo };

		try
		{
			if (OperatingSystem.IsWindows())
			{
				StartInJob(process);
			}
			else
			{
				StartTracked(process);
			}
		}
		catch
		{
			process.Dispose();
			throw;
		}

		return process;
	}

	public static bool IsTracked(Process process)
	{
		if (OperatingSystem.IsWindows())
		{
			return WindowsJobObject.Contains(process);
		}

		lock (Gate)
		{
			return Running.Contains(process);
		}
	}

	[SupportedOSPlatform(@"windows")]
	private static void StartInJob(Process process)
	{
		process.Start();

		try
		{
			WindowsJobObject.Assign(process);
		}
		catch
		{
			process.Kill(true);
			throw;
		}
	}

	private static void StartTracked(Process process)
	{
		process.EnableRaisingEvents = true;
		process.Exited += OnExited;

		// 持锁启动，避免 Exited 先于 Add 触发而留下已退出的进程
		lock (Gate)
		{
			process.Start();
			Running.Add(process);
		}
	}

	private static void OnExited(object? sender, EventArgs e)
	{
		if (sender is not Process process)
		{
			return;
		}

		lock (Gate)
		{
			Running.Remove(process);
		}
	}

	private static void KillAll()
	{
		Process[] processes;

		lock (Gate)
		{
			processes = [.. Running];
		}

		foreach (Process process in processes)
		{
			try
			{
				process.Kill(true);
			}
			catch (InvalidOperationException)
			{
				// 已经退出或已释放
			}
		}
	}
}
