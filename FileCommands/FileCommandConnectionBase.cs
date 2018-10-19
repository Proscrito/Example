using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands
{
    public abstract class FileCommandConnectionBase : FileCommandBase
    {
        protected IConnection Connection { get; }

        protected FileCommandConnectionBase(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, accountObject)
        {
            Connection = connection;
        }
    }
}
