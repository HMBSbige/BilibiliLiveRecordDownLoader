using System.Runtime.Versioning;

namespace MediaToolkit.FFmpeg;

// 保证子进程不会在本进程退出后残留：Windows 和 Linux 由系统结束子进程，本进程崩溃或被强制结束时也一样
// macOS 不支持 KillOnParentExit，设了也会被忽略，只能在正常退出时逐个结束
internal static class ChildProcessTracker
{
	private static readonly Lock Gate = new();
	private static readonly HashSet<Process> Running = [];

	static ChildProcessTracker()
	{
		if (!IsEndedBySystem)
		{
			AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
		}
	}

	[SupportedOSPlatformGuard(@"windows")]
	[SupportedOSPlatformGuard(@"linux")]
	private static bool IsEndedBySystem => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

	public static Process Start(ProcessStartInfo startInfo)
	{
		Process process = new() { StartInfo = startInfo };

		try
		{
			if (IsEndedBySystem)
			{
				startInfo.KillOnParentExit = true;
				process.Start();
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
		if (IsEndedBySystem)
		{
			return process.StartInfo.KillOnParentExit;
		}

		lock (Gate)
		{
			return Running.Contains(process);
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
