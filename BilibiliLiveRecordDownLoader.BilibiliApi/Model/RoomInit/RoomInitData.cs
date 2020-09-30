using System;

namespace BilibiliApi.Model.RoomInit
{
    [Serializable]
    public class RoomInitData
    {
        /// <summary>
        /// 真实房间号
        /// </summary>
        public long room_id { get; set; }

        /// <summary>
        /// 短房间号
        /// </summary>
        public long short_id { get; set; }

        /// <summary>
        /// 直播主站 uid
        /// </summary>
        public long uid { get; set; }
    }
}