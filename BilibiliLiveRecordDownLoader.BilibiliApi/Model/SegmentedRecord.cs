using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model
{
    [Serializable]
    public class SegmentedRecord
    {
        /// <summary>
        /// 下载地址
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// 视频大小，单位字节（Byte）
        /// </summary>
        public long size { get; set; }

        /// <summary>
        /// 视频长度，单位毫秒
        /// </summary>
        public long length { get; set; }

        /// <summary>
        /// 备用下载地址，通常为 null
        /// </summary>
        public string backup_url { get; set; }
    }
}