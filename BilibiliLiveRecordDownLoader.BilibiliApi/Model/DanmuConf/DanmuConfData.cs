namespace BilibiliApi.Model.DanmuConf
{
    public class DanmuConfData
    {
        /// <summary>
        /// 端口
        /// </summary>
        public ushort port { get; set; }

        /// <summary>
        /// 默认主机名
        /// </summary>
        public string? host { get; set; }

        /// <summary>
        /// 主机列表
        /// </summary>
        public HostServerList[]? host_server_list { get; set; }

        /// <summary>
        /// 服务器列表
        /// </summary>
        public ServerList[]? server_list { get; set; }

        /// <summary>
        /// token
        /// </summary>
        public string? token { get; set; }
    }
}