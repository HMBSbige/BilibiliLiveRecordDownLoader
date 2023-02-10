using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils;

public static class Utils
{
	public static string ToHumanBytesString(this double size)
	{
		const ushort step = 1024;
		const uint step2 = step * step;
		const uint step3 = step2 * step;
		const ulong step4 = (ulong)step3 * step;
		const ulong step5 = step4 * step;
		const ulong step6 = step5 * step;
		string mStrSize = size switch
		{
			0.0 => $@"{size:F2} Byte",
			> 0.0 and < step => $@"{size:F2} Bytes",
			>= step and < step2 => $@"{size / step:F2} KB",
			>= step2 and < step3 => $@"{size / step2:F2} MB",
			>= step3 and < step4 => $@"{size / step3:F2} GB",
			>= step4 and < step5 => $@"{size / step4:F2} TB",
			>= step5 and < step6 => $@"{size / step5:F2} PB",
			>= step6 => $@"{size / step6:F2} EB",
			_ => $@"{size}"
		};
		return mStrSize;
	}

	public static string ToHumanBytesString(this ulong size)
	{
		return ToHumanBytesString((double)size);
	}

	public static bool OpenUrl(string path)
	{
		try
		{
			new Process
			{
				StartInfo = new ProcessStartInfo(path)
				{
					UseShellExecute = true
				}
			}.Start();
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool OpenDir(string dir)
	{
		if (Directory.Exists(dir))
		{
			try
			{
				return OpenUrl(dir);
			}
			catch
			{
				// ignored
			}
		}
		return false;
	}

	public static string GetExecutablePath()
	{
		var p = Process.GetCurrentProcess();
		var res = p.MainModule?.FileName;
		if (res is not null)
		{
			return res;
		}

		var dllPath = GetDllPath();
		return Path.ChangeExtension(dllPath, @"exe");
	}

	public static string GetDllPath()
	{
		return Assembly.GetExecutingAssembly().Location;
	}

	public static void CopyToClipboard(this object obj)
	{
		try
		{
			Clipboard.SetDataObject(obj);
		}
		catch
		{
			// ignored
		}
	}

	public static string? GetAppVersion()
	{
		return typeof(App).Assembly.GetName().Version?.ToString();
	}

	public static bool ShouldIgnore(PropertyInfo propertyInfo)
	{
		if (Attribute.IsDefined(propertyInfo, typeof(IgnoreDataMemberAttribute)))
		{
			return true;
		}

		var jsonIgnore = propertyInfo.GetCustomAttribute<JsonIgnoreAttribute>();
		if (jsonIgnore is not null && jsonIgnore.Condition == JsonIgnoreCondition.Always)
		{
			return true;
		}

		return false;
	}

	public static IEnumerable<PropertyInfo> GetPropertiesExcludeJsonIgnore(this Type type)
	{
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.GetMethod is not null && p.GetMethod.IsPublic)
			.Where(p => !ShouldIgnore(p));
	}
}
