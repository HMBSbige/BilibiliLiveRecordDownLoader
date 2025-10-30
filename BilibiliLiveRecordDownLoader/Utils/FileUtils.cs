using BilibiliLiveRecordDownLoader.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace BilibiliLiveRecordDownLoader.Utils;

public class FileUtils
{
	private static readonly ILogger Logger = DI.GetLogger<FileUtils>();

	public static (ulong, ulong, ulong) GetDiskUsage(string path)
	{
		try
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				if (PInvoke.GetDiskFreeSpaceEx(path, out ulong availableFreeSpace, out ulong totalSize, out ulong totalFreeSpace))
				{
					return (availableFreeSpace, totalSize, totalFreeSpace);
				}
			}

			DriveInfo d = new(path);

			if (d.IsReady)
			{
				return ((ulong)d.AvailableFreeSpace, (ulong)d.TotalSize, (ulong)d.TotalFreeSpace);
			}
		}
		catch
		{
			// ignored
		}

		return (0, 0, 0);
	}

	public static void DeleteWithoutException(string? path)
	{
		try
		{
			if (path is null)
			{
				return;
			}

			File.Delete(path);
		}
		catch (Exception ex)
		{
			Logger.LogError(ex, $@"删除文件错误：{path}");
		}
	}

	public static bool OpenUrl(string path)
	{
		try
		{
			using Process process = new();
			process.StartInfo.UseShellExecute = true;
			process.StartInfo.FileName = path;
			process.Start();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool OpenDir(string dir)
	{
		if (!Directory.Exists(dir))
		{
			return false;
		}

		try
		{
			return OpenUrl(dir);
		}
		catch
		{
			// ignored
		}

		return false;
	}
}
