using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using FexSync.Data.Utility;
using FexSync.Data.WebSocketWatcher;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.RemoteCommands
{
    public class RemoteFileBulkProcessCommand : FileCommandConnectionBase
    {
        public delegate RemoteFileBulkProcessCommand Factory(SynchronizationObject accountObject, List<WebSocketPackage> packages);

        private static List<WebSocketPackage> PackagesBuffer { get; } = new List<WebSocketPackage>();

        private List<WebSocketPackage> Packages { get; }

        public RemoteFileBulkProcessCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject, List<WebSocketPackage> packages)
            : base(repository, connection, accountObject)
        {
            Packages = packages;
        }

        protected override async Task ExecuteInternal()
        {
            if (PackagesBuffer.Any())
            {
                Packages.AddRange(PackagesBuffer);
            }

            var history = Packages.Where(x => x.HistoryList != null).ToList();

            if (history.Any(x => x.HistoryList.Any()))
            {
                Packages.AddRange(history.SelectMany(x => x.HistoryList));
                Packages.RemoveAll(x => x.HistoryId.HasValue);
            }

            if (!Packages.Any())
            {
                return;
            }

            //root
            var itemsToProcess = Packages.Where(x => Packages.All(y => y.UploadId != x.FolderId)).ToList();

            while (itemsToProcess.Any())
            {
                foreach (var webSocketPackage in itemsToProcess)
                {
                    await Process(webSocketPackage);
                }

                //remove processed
                Packages.RemoveAll(x => itemsToProcess.Any(y => y.UploadId == x.UploadId));
                //find next level items
                var parentIdList = itemsToProcess.Where(x => x.UploadId.HasValue).Select(x => x.UploadId);
                itemsToProcess = Packages.Where(x => parentIdList.Contains(x.FolderId)).ToList();
            }

            await FileRepository.SaveAsync();
        }

        private async Task Process(WebSocketPackage webSocketPackage)
        {
            switch (webSocketPackage.ActionId)
            {
                case WebSocketAction.ObjectHistoryRenameFolder:
                case WebSocketAction.ObjectHistoryRenameUpload:
                    await ProcessMoved(webSocketPackage);
                    break;
                case WebSocketAction.ObjectHistoryDeleteFolder:
                case WebSocketAction.ObjectHistoryDeleteUpload:
                    await ProcessRemoved(webSocketPackage);
                    break;
                case WebSocketAction.ObjectHistoryCreateFolder:
                case WebSocketAction.ObjectHistoryCreateUpload:
                    await ProcessCreated(webSocketPackage);
                    break;
                case WebSocketAction.ObjectHistoryCopyFolder:
                    await ProcessCopy(webSocketPackage);
                    break;
            }

            Packages.Remove(webSocketPackage);
        }

        private async Task ProcessCopy(WebSocketPackage webSocketPackage)
        {
            //TODO: currently there is no way to guess which folder was copied, all we can do is scan this folder and download
            //TODO: it is EXTREMELY unefficient, web api SHOULD be improved
            var command = new RemoteFileScanObjectCommand(FileRepository, Connection, AccountObject, webSocketPackage);
            await command.Execute(_cancellationToken);
        }

        private async Task ProcessMoved(WebSocketPackage webSocketPackage)
        {
            var file = await FileRepository.FindAsync(AccountObject.Token, webSocketPackage.UploadId);

            if (file == null)
            {
                return;
            }

            if (file.Status == FileStatus.Synchronized)
            {
                file.Status = FileStatus.RemotelyMovedFrom;

                var newFile = await FileRepository.CloneAsync(file.Id);
                var newServerFile = await Connection.GetUploadInfoAsync(AccountObject.Token, webSocketPackage.UploadId);
                newFile.Name = newServerFile.UploadList[0].Name;
                newFile.Status = FileStatus.RemotelyMovedTo;
                newFile.MovedId = file.Id;
            }
            else
            {
                file.Status = FileStatus.Conflict;
            }
        }

        private async Task ProcessRemoved(WebSocketPackage webSocketPackage)
        {
            var file = await FileRepository.FindAsync(AccountObject.Token, webSocketPackage.UploadId);

            if (file == null)
            {
                return;
            }

            //detect moved files
            var created = await FileRepository.FindAsync(x =>
                x.Size == file.Size
                && x.Status == FileStatus.RemotelyCreated
                && x.Name == file.Name 
                && x.Crc32 == file.Crc32
                && x.UploadId != file.UploadId);

            if (created != null)
            {
                created.Status = FileStatus.RemotelyMovedTo;
                created.MovedId = file.Id;
                file.Status = FileStatus.RemotelyMovedFrom;
                file.MovedId = file.Id;
                return;
            }

            file.Status = file.Status == FileStatus.Synchronized
                ? FileStatus.RemotelyDeleted
                : FileStatus.Conflict;
        }

        private async Task ProcessCreated(WebSocketPackage webSocketPackage)
        {
            var file = await FileRepository.FindAsync(AccountObject.Token, webSocketPackage.UploadId);

            if (file != null)
            {
                return;
            }

            var remoteFile = await Connection.GetUploadInfoAsync(AccountObject.Token, webSocketPackage.UploadId);

            if (remoteFile == null || remoteFile.UploadList.Length == 0)
            {
                return;
            }

            file = new FileEntity
            {
                UploadId = webSocketPackage.UploadId,
                Crc32 = remoteFile.UploadList[0].Crc32,
                Size = remoteFile.UploadList[0].Size,
                IsFolder = remoteFile.UploadList[0].IsFolder,
                Name = remoteFile.UploadList[0].Name,
                Token = AccountObject.Token,
                Status = FileStatus.RemotelyCreated
            };

            if (webSocketPackage.FolderId.HasValue && webSocketPackage.FolderId != 0)
            {
                var remoteParent = await FileRepository.FindAsync(AccountObject.Token, webSocketPackage.FolderId);

                if (remoteParent == null)
                {
                    //there is no parent folder in the DB, let's wait for another iteration
                    PackagesBuffer.Add(webSocketPackage);
                    return;
                }

                file.ParentId = remoteParent.Id;
            }

            var oldFile = await FileRepository.FindAsync(file);

            if (oldFile != null)
            {
                if (file.UploadId == oldFile.UploadId)
                {
                    //the same file scanned twice, should never happened, but who knows
                    oldFile.Status = FileStatus.Conflict;
                    return;
                }

                file.Name = await GetIncrementName(file);
                file.Status = FileStatus.RenameRequired;
                await FileRepository.AddAsync(file);
            }
            else
            {
                //detect moved files
                var deleted = await FileRepository.FindAsync(x =>
                    x.Size == file.Size 
                    && x.Status == FileStatus.RemotelyDeleted
                    && x.Name == file.Name 
                    && x.Crc32 == file.Crc32
                    && x.UploadId != file.UploadId);

                if (deleted != null)
                {
                    deleted.Status = FileStatus.RemotelyMovedFrom;
                    deleted.MovedId = file.Id;
                    file.Status = FileStatus.RemotelyMovedTo;
                    file.MovedId = file.Id;
                }
                else
                {
                    file.Status = FileStatus.RemotelyCreated;
                }

                await FileRepository.AddAsync(file);
            }
        }
    }
}
