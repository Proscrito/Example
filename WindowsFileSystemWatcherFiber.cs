using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FexSync.Data.FileWatcher.v3
{
    public class WindowsFileSystemWatcherFiber : IDisposable
    {
        public string Path { get; set; }
        public FileSystemWatcher FileSystemWatcher { get; set; }
        public Dispatcher Dispatcher { get; set; }

        public event Action<IEnumerable<FileSystemEventArgs>> OnBulkEvent;
        public event Action<ErrorEventArgs> OnError;

        private readonly List<QueuedFileSystemEventArgs> _eventBuffer = new List<QueuedFileSystemEventArgs>();
        private readonly ConcurrentDictionary<string, QueuedFileSystemEventArgs> _queue = new ConcurrentDictionary<string, QueuedFileSystemEventArgs>();
        private bool _disposed;

        public WindowsFileSystemWatcherFiber(string path)
        {
            Path = path;

            var changeDispatcherStarted = new ManualResetEvent(false);

            void ThreadHandler()
            {
                Dispatcher = Dispatcher.CurrentDispatcher;
                changeDispatcherStarted.Set();
                Dispatcher.Run();
            }

            new Thread(ThreadHandler) { IsBackground = true }.Start();
            changeDispatcherStarted.WaitOne();

            var watcher = new FileSystemWatcher(Path)
            {
                InternalBufferSize = 1024 * 1024,
                EnableRaisingEvents = false
            };

            watcher.Error += (s, a) => Dispatcher.InvokeAsync(() => FireError(a));

            watcher.Changed += (s, a) => Dispatcher.InvokeAsync(() => FireEvent(a));
            watcher.Renamed += (s, a) => Dispatcher.InvokeAsync(() => FireEvent(a));
            watcher.Created += (s, a) => Dispatcher.InvokeAsync(() => FireEvent(a));
            watcher.Deleted += (s, a) => Dispatcher.InvokeAsync(() => FireEvent(a));

            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.DirectoryName;

            watcher.EnableRaisingEvents = true;
        }

        private void FireEvent(FileSystemEventArgs fileSystemEventArgs)
        {
            var args = new QueuedFileSystemEventArgs(fileSystemEventArgs)
            {
                EventTime = DateTime.Now
            };

            _eventBuffer.Add(args);
        }

        private void FireError(ErrorEventArgs errorEventArgs)
        {
            Logger.WriteLine($"WindowsFileSystemWatcherFiber error: {errorEventArgs.GetException().Message}");
            OnError?.Invoke(errorEventArgs);
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                while (!_disposed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    var buffer = new List<QueuedFileSystemEventArgs>();

                    //copy to local buffer from another thread
                    await Dispatcher.InvokeAsync(() => { buffer.AddRange(_eventBuffer); }, DispatcherPriority.Normal);

                    ClearOldEvents();
                    CopyToQueue(buffer);

                    await Dispatcher.InvokeAsync(() => _eventBuffer.RemoveAll(a => _queue.Values.Contains(a)), DispatcherPriority.Normal);

                    if (_queue.Count == 0)
                    {
                        //wait for incoming events
                        continue;
                    }

                    OnBulkEvent?.Invoke(_queue.Select(x => x.Value));

                    foreach (var eventArg in _queue.OrderBy(x => x.Key.Split('\\').Length))
                    {
                        //Logger.WriteLine($"WindowsFileSystemWatcherFiber dispatch real event: {eventArg.Value.ChangeType} {eventArg}");
                        _queue.TryRemove(eventArg.Key, out var dummy);
                    }
                }
            });
        }

        private void ClearOldEvents()
        {
            foreach (var eventArgs in _queue)
            {
                //do not keep in queue items older 5 minutes
                if (eventArgs.Value.EventTime.AddMinutes(5) <= DateTime.Now)
                {
                    _queue.TryRemove(eventArgs.Key, out var dummy);
                }
            }
        }

        private void CopyToQueue(IEnumerable<QueuedFileSystemEventArgs> buffer)
        {
            foreach (var eventArgs in buffer)
            {
                if (_queue.TryGetValue(eventArgs.FullPath, out var existingEventArgs))
                {
                    var actual = Prioritize(existingEventArgs, eventArgs);
                    _queue.AddOrUpdate(existingEventArgs.FullPath, actual, (s, a) => a);
                }
                else
                {
                    _queue.TryAdd(eventArgs.FullPath, eventArgs);
                }
            }
        }

        private QueuedFileSystemEventArgs Prioritize(QueuedFileSystemEventArgs existingEventArgs, QueuedFileSystemEventArgs eventArgs)
        {
            if (existingEventArgs.CompareTo(eventArgs) > 0)
            {
                return existingEventArgs;
            }

            return eventArgs;
        }

        public void Dispose()
        {
            //no multithreading expected
            if (!_disposed)
            {
                _disposed = true;
                FileSystemWatcher?.Dispose();
                Dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }
    }
}
