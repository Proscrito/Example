using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public class LocalFileBulkMovedCommand : LocalFileBulkCommandBase
    {
        public delegate LocalFileBulkMovedCommand Factory(List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject);

        public LocalFileBulkMovedCommand(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject) 
            : base(repository, fileSystemEventArgs, accountObject)
        {
        }

        protected override Task ExecuteInternal()
        {
            throw new System.NotImplementedException();
        }
    }
}
