using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.LocalCommands
{
    /// <summary>
    /// Check if conflicts are real conflicts, if not set synchronized status
    /// </summary>
    public class LocalFilesConflictsConfirmationCommand : FileCommandConnectionBase
    {
        public delegate LocalFilesConflictsConfirmationCommand Factory(SynchronizationObject accountObject);

        private List<FileEntity> _files;

        public LocalFilesConflictsConfirmationCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            _files = await FileRepository.FindAllAsync(x => x.Token == AccountObject.Token && x.Status == FileStatus.Conflict);

            foreach (var file in _files)
            {
                var relativePath = await FileRepository.GetPathAsync(file.Id);
                var fullPath = Path.Combine(AccountObject.Path, relativePath);

                if (file.IsFolder)
                {
                    CheckFolderSynchronization(file, fullPath);
                }
                else
                {
                    CheckFileSynchronization(file, fullPath);
                }
            }

            await FileRepository.SaveAsync();
        }

        private void CheckFileSynchronization(FileEntity file, string fullPath)
        {
            var fileInfo = new FileInfo(fullPath);

            if (fileInfo.Exists)
            {
                if (fileInfo.Length == file.Size)
                {
                    //TODO: probably Crc check is good here, for now only length is enough
                    //it is thje same file, allright
                    file.Status = FileStatus.Synchronized;
                }
            }
        }

        private void CheckFolderSynchronization(FileEntity file, string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                //it is directory, allright
                file.Status = FileStatus.Synchronized;
            }
        }
    }
}
