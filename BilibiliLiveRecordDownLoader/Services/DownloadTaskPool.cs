using System.Collections.Concurrent;
using System.Threading.Tasks;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Syncfusion.Data.Extensions;

namespace BilibiliLiveRecordDownLoader.Services
{
    public class DownloadTaskPool : ReactiveObject
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
        public async Task Download(LiveRecordListViewModel record, string path, double threads)
        {
            var id = record.Rid;
            var startTime = record.StartTime;

            var t = _list.GetOrAdd(id, new LiveRecordDownloadTask(id, startTime, this, path));
            this.RaisePropertyChanged(nameof(HasTaskRunning));

            t.ThreadsCount = threads;

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

        public void Remove(string id)
        {
            _list.TryRemove(id, out _);
            this.RaisePropertyChanged(nameof(HasTaskRunning));
        }

        public void StopAll()
        {
            while (HasTaskRunning)
            {
                _list.Values.ForEach(x => x.Stop());
            }
        }
    }
}
