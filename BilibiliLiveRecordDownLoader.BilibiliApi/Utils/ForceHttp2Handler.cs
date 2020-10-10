using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliApi.Utils
{
    public class ForceHttp2Handler : DelegatingHandler
    {
        public ForceHttp2Handler(HttpMessageHandler innerHandler) : base(innerHandler) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Version = HttpVersion.Version20;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
