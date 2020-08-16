using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model
{
    [Serializable]
    public class LiveRecordListData
    {
        /// <summary>
        /// 列表总数
        /// </summary>
        public long count { get; set; }

        /// <summary>
        /// 回放视频列表
        /// </summary>
        public RecordList[] list { get; set; }
    }
}