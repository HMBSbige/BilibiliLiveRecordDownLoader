using BilibiliLiveRecordDownLoader.Http.HttpPolicy;
using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BilibiliLiveRecordDownLoader.Http.DownLoaders
{
    public class MultiThreadedDownload
    {
        private int _tasksDone;
        private long _responseLength;
        private readonly ObjectPool<HttpClient> _httpClientPool;

        public string UserAgent { get; set; } = @"Mozilla/5.0 (Windows NT 6.1; Trident/7.0; rv:11.0) like Gecko";

        public string Cookie { get; set; }

        public MultiThreadedDownload()
        {
            ServicePointManager.DefaultConnectionLimit = 10000;
            _tasksDone = 0;
            var policy = new PooledHttpClientPolicy(CreateNewClient);
            _httpClientPool = new DefaultObjectPool<HttpClient>(policy, 10);
        }

        private HttpClient CreateNewClient()
        {
            var httpHandler = new SocketsHttpHandler();
            if (!string.IsNullOrEmpty(Cookie))
            {
                httpHandler.UseCookies = false;
            }

            var client = new HttpClient(new RetryHandler(httpHandler, 10), true)
            {
                MaxResponseContentBufferSize = _responseLength
            };

            if (!string.IsNullOrEmpty(Cookie))
            {
                client.DefaultRequestHeaders.Add(@"Cookie", Cookie);
            }

            client.DefaultRequestHeaders.Add(@"User-Agent", UserAgent);
            client.DefaultRequestHeaders.ConnectionClose = false;
            client.Timeout = Timeout.InfiniteTimeSpan;

            return client;
        }

        private static void SetMaxThreads()
        {
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxConcurrentActiveRequests);

            ThreadPool.SetMaxThreads(maxWorkerThreads, maxConcurrentActiveRequests);
        }

        private EventfulConcurrentQueue<FileChunk> GetTaskList(ProgressBar progress, double parts)
        {
            var asyncTasks = new EventfulConcurrentQueue<FileChunk>();

            asyncTasks.AfterItemDequeue.Subscribe(_ =>
            {
                //Tasks done holds the count of the tasks done
                //Parts *2 because there are Parts number of Enqueue AND Dequeue operations
                progress.Report(_tasksDone / (parts * 2));
            });

            asyncTasks.AfterItemEnqueue.Subscribe(_ =>
            {
                progress.Report(_tasksDone / (parts * 2));
            });

            return asyncTasks;
        }

        private static void CombineMultipleFilesIntoSingleFile(IReadOnlyCollection<FileChunk> files, string outputFilePath)
        {
            Debug.WriteLine($@"Number of files: {files.Count}.");
            var dir = Path.GetDirectoryName(outputFilePath);
            EnsureDirectory(ref dir);
            using var outputStream = File.Create(outputFilePath);
            foreach (var inputFilePath in files)
            {
                using (var inputStream = File.OpenRead(inputFilePath.TempFileName))
                {
                    // Buffer size can be passed as the second argument.
                    outputStream.Position = inputFilePath.Start;
                    inputStream.CopyTo(outputStream);
                }

                Debug.WriteLine($@"The file has been processed from {inputFilePath.Start} to {inputFilePath.End}.");
                File.Delete(inputFilePath.TempFileName);
            }
        }

        private static async Task<Stream> GetStreamAsync(Task<HttpResponseMessage> getTask)
        {
            var response = await getTask;
            return await response.Content.ReadAsStreamAsync();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Tuple<Task<Stream>, FileChunk> GetStreamTask(FileChunk piece, Uri uri, EventfulConcurrentQueue<FileChunk> asyncTasks, CancellationToken token)
        {
            var client = _httpClientPool.Get();
            try
            {
                Debug.WriteLine(@"Streaming");

                //Open a http request with the range
                var request = new HttpRequestMessage { RequestUri = uri };
                request.Headers.ConnectionClose = false;
                request.Headers.Range = new RangeHeaderValue(piece.Start, piece.End);

                //Send the request
                var responseTask = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

                var stream = GetStreamAsync(responseTask);

                //Use interlocked to increment Tasks done by one
                Interlocked.Add(ref _tasksDone, 1);
                asyncTasks.Enqueue(piece);

                var returnTuple = new Tuple<Task<Stream>, FileChunk>(stream, piece);

                return returnTuple;
            }
            finally
            {
                _httpClientPool.Return(client);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<FileChunk> GetChunkList(long partSize, long responseLength, string tempPath)
        {
            //Variable to hold the old loop end
            var previous = 0L;
            var pieces = new List<FileChunk>();

            //Loop to add all the events to the queue
            for (var i = partSize; i <= responseLength; i += partSize)
            {
                Debug.WriteLine(@"Writing Chunks...");
                if (i < responseLength - partSize)
                {
                    //Start and end values for the chunk
                    var start = previous;
                    var currentEnd = i;

                    pieces.Add(new FileChunk(start, currentEnd, tempPath));

                    //Set the start of the next loop to be the current end
                    previous = currentEnd;
                }
                else
                {
                    //Start and end values for the chunk
                    var start = previous;
                    var currentEnd = i;

                    pieces.Add(new FileChunk(start, responseLength, tempPath));

                    //Set the start of the next loop to be the current end
                    previous = currentEnd;
                }
            }

            return pieces;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task<long> GetContentLengthAsync(string url, CancellationToken token)
        {
            using var client = new HttpClient(new RetryHandler(new SocketsHttpHandler(), 3), true);
            client.DefaultRequestHeaders.Add(@"User-Agent", UserAgent);

            var result = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);

            var str = result.Content.Headers.First(h => h.Key.Equals(@"Content-Length")).Value.First();
            return long.Parse(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureDirectory(ref string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch
            {
                path = Directory.GetCurrentDirectory();
            }
        }

        public async Task DownloadFile(string url, double parts, string outFile, string tempPath = null, Action<double> onUpdate = null, CancellationToken token = default)
        {
            _tasksDone = 0;

            EventfulConcurrentQueue<FileChunk> asyncTasks;

            _responseLength = await GetContentLengthAsync(url, token);

            var partSize = (long)Math.Round(_responseLength / parts);

            EnsureDirectory(ref tempPath);

            var pieces = GetChunkList(partSize, _responseLength, tempPath);

            var uri = new Uri(url);

            Debug.WriteLine($@"{_responseLength} total size");
            Debug.WriteLine($@"{partSize} part size");

            //Set max threads to those supported by system
            SetMaxThreads();

            try
            {
                using var progress = new ProgressBar();
                if (onUpdate != null)
                {
                    progress.ProgressUpdated.Subscribe(onUpdate);
                }

                //Using custom concurrent queue to implement Enqueue and Dequeue Events
                asyncTasks = GetTaskList(progress, parts);

                Debug.WriteLine(@"Chunks done");

                var getFileChunk = new TransformManyBlock<IEnumerable<FileChunk>, FileChunk>(
                        chunk => chunk, new ExecutionDataflowBlockOptions());

                var multi = new ExecutionDataflowBlockOptions
                {
                    BoundedCapacity = Environment.ProcessorCount,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                //Gets the request stream from the file chunk 
                var getStream = new TransformBlock<FileChunk, Tuple<Task<Stream>, FileChunk>>(piece =>
                {
                    var newTask = GetStreamTask(piece, uri, asyncTasks, token);
                    return newTask;
                }, multi);

                //Writes the request stream to a temp file
                var writeStream = new ActionBlock<Tuple<Task<Stream>, FileChunk>>(async task =>
                {
                    var (streamTask, fileChunk) = task;
                    await using var stream = await streamTask;
                    await using var fileToWriteTo = new FileStream(fileChunk.TempFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 81920, true);

                    await stream.CopyToAsync(fileToWriteTo, 81920, token);

                    Interlocked.Add(ref _tasksDone, 1);
                    asyncTasks.TryDequeue(out fileChunk);
                }, multi);

                var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

                //Build the data flow pipeline
                getFileChunk.LinkTo(getStream, linkOptions);
                getStream.LinkTo(writeStream, linkOptions);

                //Post the file pieces
                getFileChunk.Post(pieces);
                getFileChunk.Complete();

                //Write all the streams
                await writeStream.Completion.ContinueWith(task =>
                {
                    //If all the tasks are done, Join the temp files
                    if (asyncTasks.Count == 0)
                    {
                        CombineMultipleFilesIntoSingleFile(pieces, outFile);
                    }
                }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Current);
            }
            catch
            {
                //Delete the temp files if there's an error
                foreach (var piece in pieces)
                {
                    var i = 0;
Start:
                    try
                    {
                        ++i;
                        File.Delete(piece.TempFileName);
                    }
                    catch (Exception) when (i < 3)
                    {
                        await Task.Delay(1000);
                        goto Start;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }
            }
        }
    }
}
