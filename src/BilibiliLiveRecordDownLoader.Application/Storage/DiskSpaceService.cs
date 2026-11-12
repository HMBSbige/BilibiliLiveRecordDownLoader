namespace BilibiliLiveRecordDownLoader.Application.Storage;

internal sealed class DiskSpaceService : IDiskSpaceService
{
	public DiskSpaceInfo? GetDiskSpace(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		try
		{
			string fullPath = Path.GetFullPath(path);
			StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			// 录制目录可能位于单独挂载的分区，取最长匹配的挂载点
			DriveInfo? drive = DriveInfo.GetDrives()
				.Where(drive => drive.IsReady && IsUnder(fullPath, drive.RootDirectory.FullName, comparison))
				.MaxBy(static drive => drive.RootDirectory.FullName.Length);

			return drive is null ? null : new DiskSpaceInfo(drive.TotalSize, drive.AvailableFreeSpace);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return null;
		}
	}

	// /mnt/data 不能匹配 /mnt/data2
	internal static bool IsUnder(string path, string root, StringComparison comparison)
	{
		return path.StartsWith(root, comparison)
			&& (path.Length == root.Length || Path.EndsInDirectorySeparator(root) || path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar);
	}
}
