using Autofac;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public class LocalFileCommandsAutofacModule : FileCommandAutofacModuleBase
    {
        protected override void Load(ContainerBuilder builder)
        {
            RegisterNonTransactionalFileCommand<LocalFileBulkCreatedCommand>(builder);
            RegisterNonTransactionalFileCommand<LocalFileBulkDeletedCommand>(builder);
            RegisterNonTransactionalFileCommand<LocalFileBulkModifiedCommand>(builder);
            RegisterNonTransactionalFileCommand<LocalFileBulkMovedCommand>(builder);
            RegisterNonTransactionalFileCommand<LocalFileBulkRenamedCommand>(builder);
            RegisterNonTransactionalFileCommand<LocalFilesConflictsConfirmationCommand>(builder);
        }
    }
}
