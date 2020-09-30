using System;

namespace BilibiliApi.Model.LiveRecordList
{
    [Serializable]
    public class LiveRecordList
    {
        /// <summary>
        /// 视频id
        /// </summary>
        public string rid { get; set; }

        /// <summary>
        /// 标题
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// 封面地址
        /// </summary>
        public string cover { get; set; }

        /// <summary>
        /// 分区名
        /// </summary>
        public string area_name { get; set; }

        /// <summary>
        /// 主分区名
        /// </summary>
        public string parent_area_name { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public long start_timestamp { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public long end_timestamp { get; set; }

        /// <summary>
        /// 人气峰值
        /// </summary>
        public long online { get; set; }

        /// <summary>
        /// 弹幕数
        /// </summary>
        public long danmu_num { get; set; }

        /// <summary>
        /// 视频长度，单位毫秒
        /// </summary>
        public long length { get; set; }
    }
}