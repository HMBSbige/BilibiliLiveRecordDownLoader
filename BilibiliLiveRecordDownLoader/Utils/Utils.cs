using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class Utils
	{
		public static string CountSize(long size)
		{
			var mStrSize = string.Empty;
			const ushort step = 1024;
			double factSize = size;
			if (factSize == 0.0)
			{
				mStrSize = $@"{factSize:F2} Byte";
			}
			else if (factSize < step)
			{
				mStrSize = $@"{factSize:F2} Bytes";
			}
			else if (factSize is >= step and < 1048576)
			{
				mStrSize = $@"{factSize / step:F2} KB";
			}
			else if (factSize is >= 1048576 and < 1073741824)
			{
				mStrSize = $@"{factSize / step / step:F2} MB";
			}
			else if (factSize is >= 1073741824 and < 1099511627776)
			{
				mStrSize = $@"{factSize / step / step / step:F2} GB";
			}
			else if (factSize >= 1099511627776)
			{
				mStrSize = $@"{factSize / step / step / step / step:F2} TB";
			}
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
			if (res != null)
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
	}
}
