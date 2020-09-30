using System;

namespace BilibiliApi.Model.DanmuConf
{
    [Serializable]
    public class ServerList
    {
        /// <summary>
        /// 主机名
        /// </summary>
        public string host { get; set; }

        /// <summary>
        /// 端口
        /// </summary>
        public ushort port { get; set; }
    }
}