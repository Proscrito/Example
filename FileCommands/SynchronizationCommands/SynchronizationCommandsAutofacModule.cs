using Autofac;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class SynchronizationCommandsAutofacModule : FileCommandAutofacModuleBase
    {
        protected override void Load(ContainerBuilder builder)
        {
            RegisterNonTransactionalFileCommand<DownloadFilesCommand>(builder);
            RegisterNonTransactionalFileCommand<UploadFilesCommand>(builder);
            RegisterNonTransactionalFileCommand<ResolveConflictFilesCommand>(builder);
            RegisterNonTransactionalFileCommand<MoveRenameFileCommand>(builder);
            RegisterNonTransactionalFileCommand<DeleteFileCommand>(builder);
            RegisterNonTransactionalFileCommand<RemoteDuplicatesRenameCommand>(builder);

            RegisterNonTransactionalFileCommand<TransferAggregationCommand>(builder);
        }
    }
}
