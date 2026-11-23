using System.Text;

namespace MediaToolkit.Tests.FFmpeg;

internal static class FakeProcess
{
	public static string Executable => OperatingSystem.IsWindows() ? @"powershell.exe" : @"/bin/sh";

	public static IReadOnlyList<string> Arguments(string powerShell, string shell)
	{
		return OperatingSystem.IsWindows()
			? [@"-NoLogo", @"-NoProfile", @"-NonInteractive", @"-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(powerShell))]
			: [@"-c", shell];
	}

	public static string CreateScript(TestDirectory directory, string name, string batch, string shell)
	{
		if (OperatingSystem.IsWindows())
		{
			return directory.CreateFile(name + @".cmd", Encoding.ASCII.GetBytes(batch));
		}

		string path = directory.CreateFile(name, Encoding.ASCII.GetBytes("#!/bin/sh\n" + shell));
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return path;
	}

	public static bool IsRunning(int processId)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}
