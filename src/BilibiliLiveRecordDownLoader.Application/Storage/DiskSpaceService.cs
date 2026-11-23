using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BilibiliLiveRecordDownLoader.Application.Storage;

// 目录可能还没建出来，两个平台都只在路径不存在时才看上一级：其他错误时上一级可能在别的分区
internal sealed partial class DiskSpaceService : IDiskSpaceService
{
	private const int ErrorFileNotFound = 2;
	private const int ErrorPathNotFound = 3;

	public DiskSpaceInfo? GetDiskSpace(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		try
		{
			string fullPath = Path.GetFullPath(path);

			return OperatingSystem.IsWindows() ? GetWindowsDiskSpace(fullPath) : GetUnixDiskSpace(fullPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return null;
		}
	}

	// UNC 共享不是盘符，DriveInfo 查不到；按目录查询还能覆盖挂载到文件夹的卷，也不用逐个探测可能已断开的驱动器
	[SupportedOSPlatform("windows")]
	private static DiskSpaceInfo? GetWindowsDiskSpace(string fullPath)
	{
		for (string? directory = fullPath; directory is not null; directory = Path.GetDirectoryName(directory))
		{
			if (GetDiskFreeSpaceEx(Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar, out ulong available, out ulong total, out _))
			{
				return new DiskSpaceInfo((long)total, (long)available);
			}

			if (Marshal.GetLastPInvokeError() is not (ErrorFileNotFound or ErrorPathNotFound))
			{
				return null;
			}
		}

		return null;
	}

	// Unix 上 DriveInfo 对任意路径做 statfs，会跟随符号链接找到实际写入的分区；按挂载点路径前缀匹配做不到
	[UnsupportedOSPlatform("windows")]
	private static DiskSpaceInfo? GetUnixDiskSpace(string fullPath)
	{
		for (string? directory = fullPath; directory is not null; directory = Path.GetDirectoryName(directory))
		{
			try
			{
				DriveInfo drive = new(directory);
				return new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
			}
			catch (DriveNotFoundException)
			{
			}
		}

		return null;
	}

	[LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}
