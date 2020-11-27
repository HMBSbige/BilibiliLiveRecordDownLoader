using BilibiliLiveRecordDownLoader.Services;
using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class Constants
	{
		public const string ParameterSetAutoRun = @"-setAutoRun";
		public const string ParameterShow = @"-show";
		public const string ParameterSilent = @"-silent";
		public const long MaxLogFileSize = 10 * 1024 * 1024; // 10MB
		public const string OutputTemplate = @"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message:lj}{NewLine}{Exception}";
		public const string LogFile = @"Logs/BilibiliLiveRecordDownLoader.log";
		public const string LiveRecordKey = @"直播回放下载";
		public const string LiveRecordPath = @"Replay";
		public const string FFmpegCopyConvert = @"-i ""{0}"" -c:v copy -c:a copy -y ""{1}""";
		public const string FFmpegSplitTo = @"-ss {0} -to {1} -accurate_seek -i ""{2}"" -codec copy -avoid_negative_ts 1 ""{3}"" -y";

		public static readonly SubjectMemorySink SubjectMemorySink = new(OutputTemplate);
		public static readonly SolidColorBrush RedBrush = new(Colors.Red);
		public static readonly SolidColorBrush GreenBrush = new(Colors.Green);
		public static readonly SolidColorBrush YellowBrush = new(Colors.Coral);
		public static readonly SolidColorBrush NormalDiskUsageBrush = new(Color.FromRgb(38, 160, 218));
	}
}
