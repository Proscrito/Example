using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{

    /// <summary>
    /// Check if conflicts are real conflicts, if not set synchronized status
    /// If yes, it tries to resolve the conflict
    /// If conflict cannot be resolved set user action required status
    /// </summary>
    public class ResolveConflictFilesCommand : FileCommandConnectionBase
    {
        public delegate ResolveConflictFilesCommand Factory(SynchronizationObject accountObject);

        private List<FileEntity> _files;

        public ResolveConflictFilesCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
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
                    await SynchronizeFolder(file, fullPath);
                }
                else
                {
                    await SynchronizeFile(file, fullPath);
                }
            }

            await FileRepository.SaveAsync();
        }

        private async Task SynchronizeFile(FileEntity file, string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                //remotely is file, locally is folder, dunno what to do
                await FileRepository.UpdateBranchStatusAsync(file, FileStatus.UserActionRequired);
                return;
            }

            var fileInfo = new FileInfo(fullPath);

            if (fileInfo.Exists)
            {
                if (fileInfo.Length == file.Size)
                {
                    //TODO: probably Crc check is good here, for now only length is enough
                    //it is thje same file, allright
                    file.Status = FileStatus.Synchronized;
                    return;
                }

                //local has higher priority
                await ReuploadFile(file, fileInfo);
            }
        }

        private async Task ReuploadFile(FileEntity file, FileInfo fileInfo)
        {
            //TODO: consider move to trash here, but deletion is fine for now
            await Connection.DeleteFileAsync(AccountObject.Token, file.UploadId);
            var parentUploadId = new long?();

            if (file.ParentId.HasValue)
            {
                var parent = await FileRepository.FindAsync(file.ParentId.Value);
                parentUploadId = parent.UploadId;
            }

            var response = await Connection.UploadAsync(AccountObject.Token, parentUploadId, fileInfo.FullName);
            file.Size = response.Size;
            file.Crc32 = response.Crc32;
            file.Status = FileStatus.Synchronized;
        }

        private async Task SynchronizeFolder(FileEntity file, string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                //it is directory, allright
                file.Status = FileStatus.Synchronized;
                return;
            }

            if (File.Exists(fullPath))
            {
                //remotely is folder, locally is file, dunno what to do
                await FileRepository.UpdateBranchStatusAsync(file, FileStatus.UserActionRequired);
                return;
            }

            Directory.CreateDirectory(fullPath);
            file.Status = FileStatus.Synchronized;
        }
    }
}
