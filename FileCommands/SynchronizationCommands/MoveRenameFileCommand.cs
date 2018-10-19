using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class MoveRenameFileCommand : FileCommandConnectionBase
    {
        public delegate MoveRenameFileCommand Factory(SynchronizationObject accountObject);

        public MoveRenameFileCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            await ProcessLocallyRenamed();
            await ProcessRemotelyMoved();

            await FileRepository.SaveAsync();
        }

        private async Task ProcessRemotelyMoved()
        {
            var movedFrom = await FileRepository.FindAllAsync(x => x.Status == FileStatus.RemotelyMovedFrom);
            var movedTo = await FileRepository.FindAllAsync(x => x.Status == FileStatus.RemotelyMovedTo);

            foreach (var fileFrom in movedFrom)
            {
                var fileTo = movedTo.FirstOrDefault(x => x.MovedId == fileFrom.Id);

                if (fileTo == null)
                {
                    fileFrom.Status = FileStatus.Conflict;
                    continue;
                }

                if (fileFrom.IsFolder)
                {
                    //move children to new dir
                    await FileRepository.UpdateAsync(x => x.ParentId == fileFrom.Id, e => new FileEntity { ParentId = fileTo.Id });
                }

                var oldPath = await FileRepository.GetPathAsync(fileFrom.Id);
                var newPath = await FileRepository.GetPathAsync(fileTo.Id);
                var oldFullPath = Path.Combine(AccountObject.Path, oldPath);
                var newFullPath = Path.Combine(AccountObject.Path, newPath);

                try
                {
                    if (fileFrom.IsFolder)
                    {
                        Directory.Move(oldFullPath, newFullPath);
                    }
                    else
                    {
                        File.Move(oldFullPath, newFullPath);
                    }

                    await FileRepository.DeleteAsync(x => x.Id == fileFrom.Id);
                    fileTo.MovedId = null;
                    fileTo.Status = FileStatus.Synchronized;
                }
                catch (IOException)
                {
                    fileFrom.Status = FileStatus.Conflict;
                    fileTo.Status = FileStatus.Conflict;
                }
            }
        }

        private async Task ProcessLocallyRenamed()
        {
            var locallyRenamed = await FileRepository.FindAllAsync(x => x.Status == FileStatus.LocallyRenamed);

            foreach (var fileEntity in locallyRenamed)
            {
                await Connection.RenameAsync(AccountObject.Token, fileEntity.UploadId, fileEntity.Name);
                fileEntity.Status = FileStatus.Synchronized;
            }
        }
    }
}
