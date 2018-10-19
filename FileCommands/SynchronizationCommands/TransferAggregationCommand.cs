using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class TransferAggregationCommand : FileCommandConnectionBase
    {
        public delegate TransferAggregationCommand Factory(SynchronizationObject accountObject);
        private readonly IList<IFileCommand> _commandsToDispose = new List<IFileCommand>();
        private bool _disposed;

        public TransferAggregationCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject) 
            : base(repository, connection, accountObject)
        {
        }

        public override bool CanExecute()
        {
            return FileRepository.IsExists(x => x.Token == AccountObject.Token 
                                                && x.Status != FileStatus.Synchronized 
                                                && x.Status != FileStatus.UserActionRequired);
        }

        protected override async Task ExecuteInternal()
        {
            await RunCommand(() => new RemoteDuplicatesRenameCommand(FileRepository, Connection, AccountObject), FileStatus.RenameRequired);
            await RunCommand(() => new DownloadFilesCommand(FileRepository, Connection, AccountObject), FileStatus.RemotelyCreated);
            await RunCommand(() => new UploadFilesCommand(FileRepository, Connection, AccountObject), FileStatus.LocallyCreated, FileStatus.LocallyModified);
            await RunCommand(() => new MoveRenameFileCommand(FileRepository, Connection, AccountObject), FileStatus.LocallyRenamed, FileStatus.RemotelyMovedFrom, FileStatus.RemotelyMovedTo);
            await RunCommand(() => new DeleteFileCommand(FileRepository, Connection, AccountObject), FileStatus.RemotelyDeleted, FileStatus.LocallyDeleted);
            await RunCommand(() => new ResolveConflictFilesCommand(FileRepository, Connection, AccountObject), FileStatus.Conflict);
        }

        private async Task RunCommand(Func<IFileCommand> factoryFunc, params FileStatus[] allowedStatuses)
        {
            var needToRun = await FileRepository.IsExistsAsync(x => allowedStatuses.ToList().Contains(x.Status));

            if (needToRun)
            {
                var command = factoryFunc();
                _commandsToDispose.Add(command);
                await command.Execute(_cancellationToken);
            }
        }

        public override void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                base.Dispose();

                foreach (var command in _commandsToDispose)
                {
                    command.Dispose();
                }
            }
        }
    }
}
