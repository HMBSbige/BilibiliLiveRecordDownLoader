using Microsoft.Extensions.ObjectPool;
using System;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Shared.HttpPolicy
{
    public class PooledHttpClientPolicy : PooledObjectPolicy<HttpClient>
    {
        private readonly Func<HttpClient> _httpClientGenerator;

        public PooledHttpClientPolicy(Func<HttpClient> httpClientGenerator)
        {
            _httpClientGenerator = httpClientGenerator;
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
