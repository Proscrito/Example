using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class DeleteFileCommand : FileCommandConnectionBase
    {
        public delegate DeleteFileCommand Factory(SynchronizationObject accountObject);


        public DeleteFileCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            await ProcessLocallyDeletedFiles();
            await ProcessRemotelyDeletedFiles();

            await FileRepository.SaveAsync();
        }

        private async Task ProcessRemotelyDeletedFiles()
        {
            var remotelyDeleted = await FileRepository.FindAllAsync(x => x.Status == FileStatus.RemotelyDeleted);
            var roots = remotelyDeleted.Where(x => remotelyDeleted.All(y => y.Id != x.ParentId));

            foreach (var fileEntity in roots)
            {
                var path = await FileRepository.GetPathAsync(fileEntity.Id);
                var fullPath = Path.Combine(AccountObject.Path, path);

                try
                {
                    if (fileEntity.IsFolder && Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }

                    if (!fileEntity.IsFolder && File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }

                    await FileRepository.DeleteAsync(x => x.Id == fileEntity.Id);
                }
                catch (IOException)
                {
                    //cannot delete
                    await FileRepository.UpdateBranchStatusAsync(fileEntity, FileStatus.UserActionRequired);
                }
            }
        }

        private async Task ProcessLocallyDeletedFiles()
        {
            var locallyDeleted = await FileRepository.FindAllAsync(x => x.Status == FileStatus.LocallyDeleted);
            var roots = locallyDeleted.Where(x => locallyDeleted.All(y => y.Id != x.ParentId));

            foreach (var fileEntity in roots)
            {
                await Connection.DeleteFileAsync(AccountObject.Token, fileEntity.UploadId);
                await FileRepository.DeleteAsync(x => x.Id == fileEntity.Id);
            }
        }
    }
}
