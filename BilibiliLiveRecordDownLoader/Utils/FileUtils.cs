using BilibiliLiveRecordDownLoader.Services;
using Microsoft.Extensions.Logging;
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
				unsafe
				{
					ulong availableFreeSpace;
					ulong totalSize;
					ulong totalFreeSpace;
					if (PInvoke.GetDiskFreeSpaceEx(path, &availableFreeSpace, &totalSize, &totalFreeSpace))
					{
						return (availableFreeSpace, totalSize, totalFreeSpace);
					}
				}
			}

			var d = new DriveInfo(path);
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
		if (path is null || !File.Exists(path))
		{
			return;
		}

		try
		{
			File.Delete(path);
		}
		catch (Exception ex)
		{
			Logger.LogWarning(ex, $@"删除文件错误：{path}");
		}
	}

	public static void DeleteFilesWithoutException(string dirPath)
	{
		try
		{
			var di = new DirectoryInfo(dirPath);
			if (di.Exists)
			{
				di.Delete(true);
			}
		}
		catch
		{
			// ignored
		}
	}
}
