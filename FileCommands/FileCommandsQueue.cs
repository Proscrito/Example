using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace FexSync.Data.FileCommands
{
    public class FileCommandsQueue : IFileCommandsQueue
    {
        private readonly ManualResetEventSlim _resetEventSlim = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _pauseEventSlim = new ManualResetEventSlim(true);
        private ConcurrentQueue<Func<IFileCommand>> _taskHighestQueue = new ConcurrentQueue<Func<IFileCommand>>();
        private ConcurrentQueue<Func<IFileCommand>> _taskNormalQueue = new ConcurrentQueue<Func<IFileCommand>>();
        private ConcurrentQueue<Func<IFileCommand>> _taskLowQueue = new ConcurrentQueue<Func<IFileCommand>>();
        private Queue<Func<IFileCommand>> _transferQueue = new Queue<Func<IFileCommand>>();
        private readonly object _transferRunLock = new object();
        private bool _disposed;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public event Action OnIterationFinished;
        public event Action OnIterationStarted;

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public FileCommandsQueue()
        {
            ProcessItems();
        }

        private void ProcessItems()
        {
            Task.Run(async () =>
            {
                while (!_disposed)
                {
                    _resetEventSlim.Wait();
                    var emptyIteration = true;

                    while (!_disposed && TryDequeue(out var factoryFunc))
                    {
                        OnIterationStarted?.Invoke();
                        _pauseEventSlim.Wait();
                        await ExecuteCommand(factoryFunc);
                        emptyIteration = false;
                    }

                    _resetEventSlim.Reset();
                    Logger.WriteLine(GetType(), $"Iteration finished. Empty: {emptyIteration}");

                    if (!emptyIteration)
                    {
                        OnIterationFinished?.Invoke();
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private async Task ExecuteCommand(Func<IFileCommand> factoryFunc)
        {
            using (var command = factoryFunc())
            {
                if (command.CanExecute())
                {
                    try
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        await command.Execute(_cancellationTokenSource.Token);
                    }
                    catch (CommandFatalException e)
                    {
                        Logger.WriteLine(GetType(), "Error executing command");
                        Logger.WriteLine(GetType(), e.GetBaseException().Message);
                    }
                }
            }
        }

        public void Enqueue(Func<IFileCommand> factoryFunc, FileCommandsQueuePriority priority = FileCommandsQueuePriority.Normal)
        {
            EnqueueInternal(factoryFunc, priority);
            _resetEventSlim.Set();
        }

        private void EnqueueInternal(Func<IFileCommand> factoryFunc, FileCommandsQueuePriority priority)
        {
            switch (priority)
            {
                case FileCommandsQueuePriority.Highest:
                    _taskHighestQueue.Enqueue(factoryFunc);
                    break;
                case FileCommandsQueuePriority.Normal:
                    _taskNormalQueue.Enqueue(factoryFunc);
                    break;
                case FileCommandsQueuePriority.Low:
                    _taskLowQueue.Enqueue(factoryFunc);
                    break;
                case FileCommandsQueuePriority.Transfer:
                    EnqueueTransfer(factoryFunc);
                    break;
                default:
                    _taskNormalQueue.Enqueue(factoryFunc);
                    break;
            }
        }

        private void EnqueueTransfer(Func<IFileCommand> factoryFunc)
        {
            lock (_transferQueue)
            {
                //TODO: add once per token
                _transferQueue.Enqueue(factoryFunc);
            }
        }

        private bool TryDequeue(out Func<IFileCommand> factoryFunc)
        {
            if (_taskHighestQueue.TryDequeue(out factoryFunc))
            {
                return true;
            }

            if (_taskNormalQueue.TryDequeue(out factoryFunc))
            {
                return true;
            }

            if (_taskLowQueue.TryDequeue(out factoryFunc))
            {
                return true;
            }

            lock (_transferRunLock)
            {
                if (_transferQueue.Count > 0)
                {
                    factoryFunc = _transferQueue.Dequeue();
                    return true;
                }
            }

            return false;
        }

        public void Pause(bool stop = false)
        {
            _pauseEventSlim.Reset();

            if (stop)
            {
                Clear();
            }
        }

        private void Clear()
        {
            _cancellationTokenSource.Cancel();
            _taskHighestQueue = new ConcurrentQueue<Func<IFileCommand>>();
            _taskNormalQueue = new ConcurrentQueue<Func<IFileCommand>>();
            _taskLowQueue = new ConcurrentQueue<Func<IFileCommand>>();

            lock (_transferRunLock)
            {
                _transferQueue = new Queue<Func<IFileCommand>>();
            }
        }

        public void Resume()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }

            _pauseEventSlim.Set();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationTokenSource.Cancel();
            _resetEventSlim?.Dispose();
        }
    }
}
