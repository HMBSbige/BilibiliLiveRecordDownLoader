using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.Interfaces
{
	public interface IHttpClient
	{
		HttpClient Client { get; set; }
	}
}
