using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity.Types;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public class LocalFileBulkDeletedCommand : LocalFileBulkCommandBase
    {
        public delegate LocalFileBulkDeletedCommand Factory(List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject);

        public LocalFileBulkDeletedCommand(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject) 
            : base(repository, fileSystemEventArgs, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            var relativePathList = GetFilteredRelatedPathList();

            foreach (var relativePath in relativePathList)
            {
                var file = await FileRepository.FindAsync(AccountObject.Token, relativePath);

                if (file == null)
                {
                    //cannot find file, do nothing, most likely the parent was deleted recursievly
                    continue;
                }

                //delete operation is the highest priority
                file.Status = FileStatus.LocallyDeleted;
            }

            await FileRepository.SaveAsync();
        }
    }
}
