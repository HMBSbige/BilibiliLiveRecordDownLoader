namespace BilibiliApi.Enums
{
    /// <summary>
    /// go-common\app\service\main\broadcast\model\operation.go
    /// </summary>
    public enum Operation
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown = -1,

        /// <summary>
        /// 心跳
        /// </summary>
        Heartbeat = 2,

        /// <summary>
        /// 心跳回应
        /// </summary>
        HeartbeatReply = 3,

        /// <summary>
        /// 收到弹幕
        /// </summary>
        SendMsgReply = 5,

        /// <summary>
        /// 进房
        /// </summary>
        Auth = 7,

        /// <summary>
        /// 进房回应
        /// </summary>
        AuthReply = 8,
    }
}
