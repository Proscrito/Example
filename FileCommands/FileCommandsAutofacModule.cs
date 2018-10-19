using Autofac;

namespace FexSync.Data.FileCommands
{
    public class FileCommandsAutofacModule : FileCommandAutofacModuleBase
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<FileCommandsQueue>().As<IFileCommandsQueue>().SingleInstance();
            RegisterNonTransactionalFileCommand<FileCommandActionWrapper>(builder);
        }
    }
}
