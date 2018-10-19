using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class RemoteDuplicatesRenameCommand : FileCommandConnectionBase
    {
        public delegate RemoteDuplicatesRenameCommand Factory(SynchronizationObject accountObject);

        public RemoteDuplicatesRenameCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            var needRename = await FileRepository.FindAllAsync(x => x.Status == FileStatus.RenameRequired);

            foreach (var fileEntity in needRename)
            {
                await Connection.RenameAsync(AccountObject.Token, fileEntity.UploadId, fileEntity.Name);
                fileEntity.Status = FileStatus.RemotelyCreated;
            }

            await FileRepository.SaveAsync();
        }
    }
}
