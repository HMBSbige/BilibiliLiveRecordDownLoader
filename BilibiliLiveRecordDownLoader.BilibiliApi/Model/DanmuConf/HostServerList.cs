using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model.DanmuConf
{
    [Serializable]
    public class HostServerList
    {
        /// <summary>
        /// 主机名
        /// </summary>
        public string host { get; set; }

        /// <summary>
        /// TCP 端口
        /// </summary>
        public ushort port { get; set; }

        /// <summary>
        /// wss 端口
        /// </summary>
        public ushort wss_port { get; set; }

        /// <summary>
        /// ws 端口
        /// </summary>
        public ushort ws_port { get; set; }
    }
}