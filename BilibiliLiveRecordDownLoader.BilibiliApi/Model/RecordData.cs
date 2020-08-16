﻿using System;

namespace BilibiliLiveRecordDownLoader.BilibiliApi.Model
{
    [Serializable]
    public class RecordData
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
        public SegmentedRecord[] list { get; set; }
    }
}