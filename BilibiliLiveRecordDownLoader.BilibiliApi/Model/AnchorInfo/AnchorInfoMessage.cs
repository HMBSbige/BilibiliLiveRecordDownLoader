namespace BilibiliApi.Model.AnchorInfo
{
    public class AnchorInfoMessage
    {
        /// <summary>
        /// 正常返回 0
        /// </summary>
        public int code { get; set; }

        /// <summary>
        /// 正常返回 "success"，否则返回错误信息
        /// </summary>
        public string? msg { get; set; }

        /// <summary>
        /// 正常返回 "success"，否则返回错误信息
        /// </summary>
        public string? message { get; set; }

        /// <summary>
        /// 主播信息
        /// </summary>
        public AnchorInfoData? data { get; set; }
    }
}
