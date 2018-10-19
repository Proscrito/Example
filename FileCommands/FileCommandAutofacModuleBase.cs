using Autofac;
using FexSync.Data.Repository;
using FexSync.Data.Utility.Autofac;

namespace FexSync.Data.FileCommands
{
    public abstract class FileCommandAutofacModuleBase: Module
    {
        protected void RegisterNonTransactionalFileCommand<T>(ContainerBuilder builder)
            where T : FileCommandBase
        {
            builder.RegisterType<T>()
                .WithParameter(
                    (p, c) => p.ParameterType == typeof(IFileRepository),
                    (p, c) => c.ResolveNamed<IFileRepository>(AutofacNamedRegistrations.FileRepositoryNonTransactional));
        }
    }
}
