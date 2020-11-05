using System;
using System.Net.Http;
using Microsoft.Extensions.ObjectPool;

namespace BilibiliLiveRecordDownLoader.Shared.HttpPolicy
{
    public class PooledHttpClientPolicy : PooledObjectPolicy<HttpClient>
    {
        private readonly Func<HttpClient> _httpClientGenerator;

        public PooledHttpClientPolicy(Func<HttpClient> httpClientGenerator)
        {
            _httpClientGenerator = httpClientGenerator ?? (() => new HttpClient());
        }

        public override HttpClient Create()
        {
            return _httpClientGenerator();
        }

        public override bool Return(HttpClient obj)
        {
            return true;
        }
    }
}
