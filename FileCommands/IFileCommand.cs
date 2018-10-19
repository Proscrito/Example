using System;
using System.Threading;
using System.Threading.Tasks;

namespace FexSync.Data.FileCommands
{
    public interface IFileCommand : IDisposable
    {
        bool CanExecute();
        Task Execute();
        Task Execute(CancellationToken cancellationToken);
        event Action<FileCommandStatus> OnStatusChanged;
    }
}
