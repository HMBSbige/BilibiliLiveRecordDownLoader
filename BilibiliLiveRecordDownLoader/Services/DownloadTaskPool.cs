using System.Collections.Concurrent;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.ViewModels;

namespace BilibiliLiveRecordDownLoader.Services
{
    public class DownloadTaskPool
    {
        private readonly ConcurrentDictionary<string, LiveRecordDownloadTask> _list;

        public bool HasTaskRunning => !_list.IsEmpty;

        public DownloadTaskPool()
        {
            _list = new ConcurrentDictionary<string, LiveRecordDownloadTask>();
        }

        /// <summary>
        /// record 开始/停止下载
        /// </summary>
        public async Task Download(LiveRecordListViewModel record, string path)
        {
            var id = record.Rid;
            var t = _list.GetOrAdd(id, new LiveRecordDownloadTask(id, _list, path));
            record.Attach(t);
            await record.StartOrStop();
        }

        public void Attach(LiveRecordListViewModel record)
        {
            var id = record.Rid;
            if (_list.TryGetValue(id, out var t))
            {
                record.Attach(t);
            }
        }
    }
}
