using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public static class Constants
	{
		public const string ParameterShow = @"-show";
		public const string ParameterSilent = @"-silent";
		public const long MaxLogFileSize = 10 * 1024 * 1024; // 10MB
		public const string OutputTemplate = @"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message:lj}{NewLine}{Exception}";
		public const string LogFile = @"Logs/BilibiliLiveRecordDownLoader.log";
		public const string LiveRecordPath = @"Replay";
		public const string FFmpegCopyConvert = @"-i ""{0}"" -c:v copy -c:a copy -y ""{1}""";
		public const string FFmpegVideoAudioConvert = @"-i ""{0}"" -i ""{1}"" -vcodec copy -acodec copy ""{2}"" -y";
		public const string FFmpegSplitTo = @"-ss {0} -to {1} -accurate_seek -i ""{2}"" -codec copy -avoid_negative_ts 1 ""{3}"" -y";

		public static readonly SolidColorBrush NormalBlueBrush = new(Color.FromRgb(38, 160, 218));
		public static readonly SolidColorBrush RedBrush = new(Colors.Red);
		public static readonly SolidColorBrush GreenBrush = NormalBlueBrush;
		public static readonly SolidColorBrush YellowBrush = new(Colors.Coral);
		public static readonly SolidColorBrush NormalDiskUsageBrush = NormalBlueBrush;

		public const string VideoFilter = @"视频文件|*.mp4;*.flv;*.mkv" + @"|" + AllFilter;
		public const string AllFilter = @"所有文件|*.*";

		public const string Qn20000 = @"4K(20000)";
		public const string Qn10000 = @"原画(10000)";
		public const string Qn401 = @"蓝光(杜比)(401)";
		public const string Qn400 = @"蓝光(400)";
		public const string Qn250 = @"超清(250)";
		public const string Qn150 = @"高清(150)";
		public const string Qn80 = @"流畅(80)";
	}
}
