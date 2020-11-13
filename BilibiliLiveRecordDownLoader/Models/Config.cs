using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System;

namespace BilibiliLiveRecordDownLoader.Models
{
    public class Config : MyReactiveObject
    {
        #region 字段

        private long _roomId = 732;
        private string _mainDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        private byte _downloadThreads = 8;

        #endregion

        #region 属性

        public long RoomId
        {
            get => _roomId;
            set => this.RaiseAndSetIfChanged(ref _roomId, value);
        }

        public string MainDir
        {
            get => _mainDir;
            set => this.RaiseAndSetIfChanged(ref _mainDir, value);
        }

        public byte DownloadThreads
        {
            get => Math.Max((byte)1, _downloadThreads);
            set => this.RaiseAndSetIfChanged(ref _downloadThreads, value);
        }

        #endregion
    }
}
