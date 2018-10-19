using System;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;

namespace FexSync.Data.FileCommands
{
    public class FileCommandActionWrapper : FileCommandBase
    {
        public delegate FileCommandActionWrapper Factory(SynchronizationObject accountObject, Action callback);

        private readonly Action _callback;

        public FileCommandActionWrapper(IFileRepository repository, SynchronizationObject accountObject, Action callback)
            : base(repository, accountObject)
        {
            _callback = callback;
        }

        protected override async Task ExecuteInternal()
        {
            await Task.Run(_callback, _cancellationToken);
        }
    }
}
