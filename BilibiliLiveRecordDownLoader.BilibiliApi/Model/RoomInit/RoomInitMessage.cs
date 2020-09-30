using System;

namespace BilibiliApi.Model.RoomInit
{
    [Serializable]
    public class RoomInitMessage
    {
        /// <summary>
        /// 正常返回 0
        /// </summary>
        public long code { get; set; }

        /// <summary>
        /// 正常返回 "ok"，否则返回错误信息
        /// </summary>
        public string msg { get; set; }

        /// <summary>
        /// 正常返回 "ok"，否则返回错误信息
        /// </summary>
        public string message { get; set; }

        /// <summary>
        /// 房间信息
        /// </summary>
        public RoomInitData data { get; set; }
    }
}
