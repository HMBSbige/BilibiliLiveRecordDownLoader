using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BilibiliLiveRecordDownLoader.Application.Storage;

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

			if (OperatingSystem.IsWindows())
			{
				return GetWindowsDiskSpace(fullPath);
			}

			StringComparison comparison = OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			DriveInfo? drive = FindReadyMount(DriveInfo.GetDrives(), static drive => drive.RootDirectory.FullName, static drive => drive.IsReady, fullPath, comparison);

			return drive is null ? null : new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return null;
		}
	}

	// 只探测最长匹配的这一个挂载点，不相关的网络挂载没有响应时不会卡住；它没就绪时也不退回到上层挂载点，那样报出的是别的分区的空间
	internal static T? FindReadyMount<T>(IEnumerable<T> mounts, Func<T, string> root, Func<T, bool> isReady, string path, StringComparison comparison) where T : class
	{
		T? mount = mounts.Where(mount => IsUnder(path, root(mount), comparison)).MaxBy(mount => root(mount).Length);
		return mount is not null && isReady(mount) ? mount : null;
	}

	// /mnt/data 不能匹配 /mnt/data2
	internal static bool IsUnder(string path, string root, StringComparison comparison)
	{
		return path.StartsWith(root, comparison)
			&& (path.Length == root.Length || Path.EndsInDirectorySeparator(root) || path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar);
	}

	// UNC 共享不是盘符，DriveInfo 查不到；按目录查询还能覆盖挂载到文件夹的卷，也不用逐个探测可能已断开的驱动器
	[SupportedOSPlatform("windows")]
	private static DiskSpaceInfo? GetWindowsDiskSpace(string fullPath)
	{
		// 目录可能还没建出来
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

	[LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}
