namespace BilibiliLiveRecordDownLoader.Application.Common;

internal static partial class FileOperations
{
	// Windows 和 macOS 默认的文件系统不区分大小写
	public static StringComparison PathComparison { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	public static bool TryDelete(string path, ILogger logger)
	{
		try
		{
			File.Delete(path);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogDeleteFailed(logger, exception, path);
			return false;
		}
	}

	public static void TryDeleteDirectory(string path, ILogger logger)
	{
		try
		{
			Directory.Delete(path, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			LogDeleteFailed(logger, exception, path);
		}
	}

	public static bool IsSamePath(string path1, string path2)
	{
		return string.Equals(Path.GetFullPath(path1), Path.GetFullPath(path2), PathComparison);
	}

	public static string GetQueueKey(string path)
	{
		return Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete {Path}")]
	private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
