using Autofac;
using FexSync.Data.Repository;

namespace FexSync.Data.FileCommands.RemoteCommands
{
    public class RemoteFileCommandsAutofacModule : FileCommandAutofacModuleBase
    {
        protected override void Load(ContainerBuilder builder)
        {
            RegisterNonTransactionalFileCommand<RemoteFileScanObjectCommand>(builder);
            RegisterNonTransactionalFileCommand<RemoteFileBulkProcessCommand>(builder);
        }
    }
}
