using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class AutoStartService : IAutoStartService
{
	private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string MacLaunchAgentLabel = "io.github.hmbsbige.bilibililiverecorddownloader";

	public bool IsSupported => Environment.ProcessPath is not null && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());

	private static string ExecutablePath => Environment.ProcessPath ?? throw new PlatformNotSupportedException();

	public bool IsEnabled()
	{
		// 程序换了目录后旧的登记已经失效
		if (OperatingSystem.IsWindows())
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(WindowsRunKey);
			return key?.GetValue(AppConsts.Name) is string command && command.Equals(WindowsCommand, StringComparison.OrdinalIgnoreCase);
		}

		return GetStartupFilePath() is { } path && File.Exists(path) && File.ReadAllText(path) == CreateStartupFile();
	}

	public void SetEnabled(bool enabled)
	{
		if (OperatingSystem.IsWindows())
		{
			SetWindowsEnabled(enabled);
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

	private static string WindowsCommand => $"\"{ExecutablePath}\" {AppStartupOptions.MinimizedArgument}";

	private static string CreateStartupFile()
	{
		return OperatingSystem.IsMacOS() ? CreateLaunchAgent() : CreateDesktopEntry();
	}

	[SupportedOSPlatform("windows")]
	private static void SetWindowsEnabled(bool enabled)
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
	}

	private static string? GetStartupFilePath()
	{
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (OperatingSystem.IsMacOS())
		{
			return Path.Combine(home, "Library", "LaunchAgents", $"{MacLaunchAgentLabel}.plist");
		}

		if (OperatingSystem.IsLinux())
		{
			string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config");
			return Path.Combine(configHome, "autostart", $"{AppConsts.Name}.desktop");
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

	// 引号内的 " ` $ \ 要转义，% 写成 %%；Exec 的值本身又是字符串，反斜杠还要再转义一次
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
}
