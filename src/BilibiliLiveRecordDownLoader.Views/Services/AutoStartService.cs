using Microsoft.Win32;
using System.Security;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed partial class AutoStartService(ILogger<AutoStartService> logger) : IAutoStartService
{
	private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string MacLaunchAgentLabel = "io.github.hmbsbige.bilibililiverecorddownloader";

	public bool IsSupported => Environment.ProcessPath is not null && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

	private static string ExecutablePath => Environment.ProcessPath ?? throw new PlatformNotSupportedException();

	// 启动时就要读，不能让启动失败
	public bool IsEnabled()
	{
		try
		{
			// 程序换了目录后旧的登记已经失效；旧版参数不同的登记指向当前程序时照样按最小化启动
			if (OperatingSystem.IsWindows())
			{
				using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WindowsRunKey);
				return key?.GetValue(AppConsts.Name) is string command && (command.Equals(WindowsCommand, StringComparison.OrdinalIgnoreCase) || command.Equals(LegacyWindowsCommand, StringComparison.OrdinalIgnoreCase));
			}

			return GetStartupFilePath() is { } path && File.Exists(path) && File.ReadAllText(path) == CreateStartupFile();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
		{
			LogReadFailed(exception);
			return false;
		}
	}

	public void SetEnabled(bool enabled)
	{
		if (OperatingSystem.IsWindows())
		{
			using RegistryKey key = Registry.CurrentUser.CreateSubKey(WindowsRunKey);

			if (enabled)
			{
				key.SetValue(AppConsts.Name, WindowsCommand);
			}
			else
			{
				key.DeleteValue(AppConsts.Name, false);
			}

			return;
		}

		string path = GetStartupFilePath() ?? throw new PlatformNotSupportedException();

		if (!enabled)
		{
			File.Delete(path);
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new PlatformNotSupportedException());
		File.WriteAllText(path, CreateStartupFile());
	}

	public void TakeOverExisting()
	{
		if (!IsSupported)
		{
			return;
		}

		try
		{
			if (IsRegistered() && !IsEnabled())
			{
				SetEnabled(true);
				LogTakenOver();
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
		{
			LogTakeOverFailed(exception);
		}
	}

	private static bool IsRegistered()
	{
		if (OperatingSystem.IsWindows())
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WindowsRunKey);
			return key?.GetValue(AppConsts.Name) is not null;
		}

		return GetStartupFilePath() is { } path && File.Exists(path);
	}

	private static string WindowsCommand => $"\"{ExecutablePath}\" {AppStartupOptions.MinimizedArgument}";

	private static string LegacyWindowsCommand => $"\"{ExecutablePath}\" {AppStartupOptions.LegacySilentArgument}";

	private static string CreateStartupFile()
	{
		return OperatingSystem.IsMacOS() ? CreateLaunchAgent() : CreateDesktopEntry();
	}

	private static string? GetStartupFilePath()
	{
		if (OperatingSystem.IsMacOS())
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{MacLaunchAgentLabel}.plist");
		}

		if (OperatingSystem.IsLinux())
		{
			// ApplicationData 在 Linux 上按 XDG 规范取 XDG_CONFIG_HOME，未设置或不是绝对路径时用 ~/.config；目录可能还没建，写入时再创建
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify), "autostart", $"{AppConsts.Name}.desktop");
		}

		return null;
	}

	private static string CreateDesktopEntry()
	{
		return $"""
			[Desktop Entry]
			Type=Application
			Name={AppConsts.Name}
			Exec={QuoteDesktopExecArgument(ExecutablePath)} {AppStartupOptions.MinimizedArgument}
			X-GNOME-Autostart-enabled=true

			""";
	}

	// Exec 的值本身又是字符串，加引号转义后的反斜杠还要整体再转义一次
	private static string QuoteDesktopExecArgument(string value)
	{
		StringBuilder builder = new("\"");

		foreach (char c in value)
		{
			if (c is '"' or '`' or '$' or '\\')
			{
				builder.Append('\\');
			}

			builder.Append(c is '%' ? "%%" : c);
		}

		return builder.Append('"').ToString().Replace("\\", "\\\\", StringComparison.Ordinal);
	}

	private static string CreateLaunchAgent()
	{
		return $"""
			<?xml version="1.0" encoding="UTF-8"?>
			<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
			<plist version="1.0">
			<dict>
				<key>Label</key>
				<string>{MacLaunchAgentLabel}</string>
				<key>ProgramArguments</key>
				<array>
					<string>{SecurityElement.Escape(ExecutablePath)}</string>
					<string>{AppStartupOptions.MinimizedArgument}</string>
				</array>
				<key>RunAtLoad</key>
				<true/>
			</dict>
			</plist>

			""";
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to read the auto start registration")]
	private partial void LogReadFailed(Exception exception);

	[LoggerMessage(Level = LogLevel.Information, Message = "The existing auto start registration now starts this program")]
	private partial void LogTakenOver();

	[LoggerMessage(Level = LogLevel.Warning, Message = "Failed to take over the auto start registration")]
	private partial void LogTakeOverFailed(Exception exception);
}
