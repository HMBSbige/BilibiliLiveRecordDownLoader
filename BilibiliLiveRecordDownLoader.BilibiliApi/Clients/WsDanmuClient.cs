using BilibiliApi.Model.DanmuConf;
using Microsoft.Extensions.Logging;

namespace BilibiliApi.Clients
{
    public class WsDanmuClient : WssDanmuClient
    {
        protected override string Server => $@"ws://{Host}:{Port}/sub";
        protected override ushort DefaultPort => 2244;

        public WsDanmuClient(ILogger logger) : base(logger) { }

        protected override ushort GetPort(HostServerList server)
        {
            return server.ws_port;
        }
    }
}