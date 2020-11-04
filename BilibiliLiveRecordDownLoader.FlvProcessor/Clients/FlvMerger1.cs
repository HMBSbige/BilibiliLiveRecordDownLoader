using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.FlvProcessor.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.FlvProcessor.Clients
{
    public class FlvMerger1 : IFlvMerger
    {
        private readonly ILogger _logger;
        private static readonly ArrayPool<byte> ArrayPool = ArrayPool<byte>.Shared;

        //TODO
        private readonly BehaviorSubject<double> _progressUpdated = new BehaviorSubject<double>(0.0);
        public IObservable<double> ProgressUpdated => _progressUpdated.AsObservable();

        //TODO
        private readonly BehaviorSubject<double> _currentSpeed = new BehaviorSubject<double>(0.0);
        public IObservable<double> CurrentSpeed => _currentSpeed.AsObservable();

        public int BufferSize { get; set; } = 4096;

        public bool IsAsync { get; set; } = false;

        private readonly List<string> _files = new List<string>();
        public IEnumerable<string> Files => _files;

        public FlvMerger1(ILogger<FlvMerger1> logger)
        {
            _logger = logger;
        }

        public void Add(string path)
        {
            _files.Add(path);
        }

        public void AddRange(IEnumerable<string> path)
        {
            _files.AddRange(path);
        }

        public async ValueTask MergeAsync(string path, CancellationToken token)
        {
            await using var outFile = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, IsAsync);

            var header = new FlvHeader();
            await using (var f0 = new FileStream(Files.First(), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                var b = ArrayPool.Rent(header.Size);
                try
                {
                    var memory = b.AsMemory(0, header.Size);

                    // 读 Header
                    f0.Read(memory.Span);
                    header.Read(memory.Span);

                    // 写 header
                    outFile.Write(memory.Span);
                }
                finally
                {
                    ArrayPool.Return(b);
                }
            }
            //TODO
        }
    }
}