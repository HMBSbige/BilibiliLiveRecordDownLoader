using System.Net.Http.Headers;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
	public class FileRange
	{
		public RangeHeaderValue Range { get; init; }
		public string FileName { get; init; }

		public FileRange(RangeHeaderValue range, string fileName)
		{
			Range = range;
			FileName = fileName;
		}
	}
}
