using System;

namespace BilibiliApi.Model.LiveRecordUrl
{
    [Serializable]
    public class LiveRecordUrlData
    {
        /// <summary>
        /// 视频大小，单位字节（Byte）
        /// </summary>
        public long size { get; set; }

        /// <summary>
        /// 视频长度，单位毫秒
        /// </summary>
        public long length { get; set; }

        /// <summary>
        /// 分段视频信息
        /// </summary>
        public LiveRecordUrl[] list { get; set; }
    }
}