using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model.LiveRecordUrl
{
    [Serializable]
    public class LiveRecordUrlMessage
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
        /// 视频信息
        /// </summary>
        public LiveRecordUrlData data { get; set; }
    }
}
