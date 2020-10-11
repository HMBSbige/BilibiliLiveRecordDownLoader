using System;
using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public sealed class EventfulConcurrentQueue<T> : IDisposable
    {
        private readonly ConcurrentQueue<T> _queue;

        private readonly Subject<Unit> _itemEnqueue = new Subject<Unit>();
        public IObservable<Unit> AfterItemEnqueue => _itemEnqueue.AsObservable();

        private readonly Subject<Unit> _itemDequeue = new Subject<Unit>();
        public IObservable<Unit> AfterItemDequeue => _itemDequeue.AsObservable();

        public EventfulConcurrentQueue()
        {
            _queue = new ConcurrentQueue<T>();
        }

        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
            _itemEnqueue.OnNext(Unit.Default);
        }

        public int Count => _queue.Count;

        public bool TryDequeue(out T result)
        {
            var success = _queue.TryDequeue(out result);

            if (success)
            {
                _itemDequeue.OnNext(Unit.Default);
            }

            return success;
        }

        public void Dispose()
        {
            _itemEnqueue.OnCompleted();
            _itemDequeue.OnCompleted();
        }
    }
}
