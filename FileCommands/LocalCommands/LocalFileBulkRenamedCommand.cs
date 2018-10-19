using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity.Types;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public class LocalFileBulkRenamedCommand : LocalFileBulkCommandBase
    {
        public delegate LocalFileBulkRenamedCommand Factory(List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject);

        public LocalFileBulkRenamedCommand(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject)
            : base(repository, fileSystemEventArgs, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            var renamedEventArgs = FileSystemEventArgs
                .OfType<RenamedEventArgs>()
                .ToList();

            foreach (var renamedEventArg in renamedEventArgs)
            {
                var oldRelativePath = renamedEventArg.OldFullPath.Replace(AccountObject.Path, "")
                    .Trim(Path.DirectorySeparatorChar);
                var newRelativePath = renamedEventArg.FullPath.Replace(AccountObject.Path, "")
                    .Trim(Path.DirectorySeparatorChar);

                if (AccountObject.IgnoredFolders.Any(y =>
                    oldRelativePath.StartsWith(y, StringComparison.InvariantCultureIgnoreCase)))
                {
                    return;
                }

                var file = await FileRepository.FindAsync(AccountObject.Token, oldRelativePath);

                if (file == null)
                {
                    //something wrong
                    return;
                }
                
                file.Name = Path.GetFileName(newRelativePath);
                file.Status = FileStatus.LocallyRenamed;

                if (!file.IsFolder)
                {
                    file.LastWrite = File.GetLastWriteTime(Path.Combine(AccountObject.Path, newRelativePath)).Ticks;
                }
            }

            await FileRepository.SaveAsync();
        }
    }
}
