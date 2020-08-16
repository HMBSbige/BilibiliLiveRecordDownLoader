using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model
{
    [Serializable]
    public class LiveRecordListMessage
    {
        /// <summary>
        /// 正常返回 0
        /// </summary>
        public long code { get; set; }

        /// <summary>
        /// 正常返回 "0"，否则返回错误信息
        /// </summary>
        public string message { get; set; }

        /// <summary>
        /// 回放视频信息
        /// </summary>
        public LiveRecordListData data { get; set; }
    }
}
