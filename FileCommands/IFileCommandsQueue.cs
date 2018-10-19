using System;

namespace FexSync.Data.FileCommands
{
    public interface IFileCommandsQueue : IDisposable
    {
        void Enqueue(Func<IFileCommand> factoryFunc, FileCommandsQueuePriority priority = FileCommandsQueuePriority.Normal);

        void Pause(bool stop = false);

        void Resume();

        event Action OnIterationStarted;

        event Action OnIterationFinished;
    }
}