using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class Utils
	{
		public static string CountSize(long size)
		{
			const ushort step = 1024;
			const int step2 = step * step;
			const int step3 = step2 * step;
			const long step4 = (long)step3 * step;
			const long step5 = step4 * step;
			const long step6 = step5 * step;
			double factSize = size >= 0 ? size : (ulong)size;
			var mStrSize = factSize switch
			{
				0.0 => $@"{factSize:F2} Byte",
				> 0.0 and < step => $@"{factSize:F2} Bytes",
				>= step and < step2 => $@"{factSize / step:F2} KB",
				>= step2 and < step3 => $@"{factSize / step2:F2} MB",
				>= step3 and < step4 => $@"{factSize / step3:F2} GB",
				>= step4 and < step5 => $@"{factSize / step4:F2} TB",
				>= step5 and < step6 => $@"{factSize / step5:F2} PB",
				>= step6 => $@"{factSize / step6:F2} EB",
				_ => $@"{size}"
			};
			return mStrSize;
		}

		public static (long, long) GetDiskUsage(string path)
		{
			try
			{
				var allDrives = DriveInfo.GetDrives();
				foreach (var d in allDrives)
				{
					if (d.Name != Path.GetPathRoot(path))
					{
						continue;
					}

					if (d.IsReady)
					{
						return (d.AvailableFreeSpace, d.TotalSize);
					}
				}
			}
			catch
			{
				// ignored
			}

			return (0, 0);
		}

		public static bool OpenUrl(string path)
		{
			try
			{
				new Process { StartInfo = new ProcessStartInfo(path) { UseShellExecute = true } }.Start();
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

		public static void CopyToClipboard(object obj)
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

		public static bool DeleteFiles(string path)
		{
			try
			{
				var di = new DirectoryInfo(path);
				di.Delete(true);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public static string? GetAppVersion()
		{
			return typeof(App).Assembly.GetName().Version?.ToString();
		}

		public static IEnumerable<string> GetPropertiesNameExcludeJsonIgnore(this Type type)
		{
			return type.GetProperties()
				.Where(pi => !Attribute.IsDefined(pi, typeof(JsonIgnoreAttribute)) && !Attribute.IsDefined(pi, typeof(IgnoreDataMemberAttribute)))
				.Select(p => p.Name);
		}
	}
}
