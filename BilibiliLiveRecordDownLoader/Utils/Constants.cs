using BilibiliLiveRecordDownLoader.Services;

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

		public static readonly SubjectMemorySink SubjectMemorySink = new(OutputTemplate);
	}
}
