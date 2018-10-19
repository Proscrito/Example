using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using FexSync.Data.WebSocketWatcher;
using Net.Fex.Api;
using Net.Fex.Api.ConnectionCommands.DataContracts;

namespace FexSync.Data.FileCommands.RemoteCommands
{
    public class RemoteFileScanObjectCommand : FileCommandConnectionBase
    {
        public delegate RemoteFileScanObjectCommand Factory(SynchronizationObject accountObject);

        private readonly WebSocketPackage _webSocketPackage;

        public RemoteFileScanObjectCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject)
            : base(repository, connection, accountObject)
        {
        }

        internal RemoteFileScanObjectCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject, WebSocketPackage webSocketPackage)
            : base(repository, connection, accountObject)
        {
            _webSocketPackage = webSocketPackage;
        }

        public override bool CanExecute()
        {
            //if historyId is not null - no need to scan whole tree
            //server watcher will do the job
            return AccountObject.HistoryId == 0 || _webSocketPackage != null;
        }

        protected override async Task ExecuteInternal()
        {
            var objects = await GetRootObjects();

            var currentLevelObjects = new List<Tuple<ObjectViewResponse, ObjectViewResponse>>();
            currentLevelObjects.AddRange(objects);

            while (currentLevelObjects.Any())
            {
                var nextLevelObjects = new List<Tuple<ObjectViewResponse, ObjectViewResponse>>();

                foreach (var currentLevelObject in currentLevelObjects)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    await AddToRepository(currentLevelObject);

                    if (currentLevelObject.Item1.IsFolder)
                    {
                        var children = await Connection.ObjectFolderViewAsync(AccountObject.Token, currentLevelObject.Item1.UploadId);
                        nextLevelObjects.AddRange(children.UploadList.Select(x => new Tuple<ObjectViewResponse, ObjectViewResponse>(x, currentLevelObject.Item1)));
                    }
                }

                currentLevelObjects = nextLevelObjects;
            }

            await FileRepository.SaveAsync();
        }

        private async Task<List<Tuple<ObjectViewResponse, ObjectViewResponse>>> GetRootObjects()
        {
            var objects = new List<Tuple<ObjectViewResponse, ObjectViewResponse>>();

            if (_webSocketPackage != null)
            {
                //scan only 1 folder
                ObjectViewResponse parentServerFolder = null;

                if (_webSocketPackage.FolderId != 0)
                {
                    var parentFolder = await Connection.GetUploadInfoAsync(AccountObject.Token, _webSocketPackage.FolderId);

                    var rootId = parentFolder.UploadList[0].FolderId;
                    var root = await Connection.ObjectFolderViewAsync(AccountObject.Token, rootId);
                    parentServerFolder = root.UploadList.FirstOrDefault(x => x.UploadId == _webSocketPackage.FolderId);
                }

                var serverFolder = (await Connection.ObjectFolderViewAsync(AccountObject.Token, _webSocketPackage.FolderId))
                    .UploadList
                    .FirstOrDefault(x => x.UploadId == _webSocketPackage.UploadId);

                objects.Add(new Tuple<ObjectViewResponse, ObjectViewResponse>(serverFolder, parentServerFolder));
            }
            else
            {
                //scan whole object
                var props = await Connection.ObjectViewAsync(AccountObject.Token);
                var objectsToAdd = props.UploadList
                    .Where(x => !AccountObject.IgnoredFolders.Any(y => x.Name.StartsWith(y, StringComparison.InvariantCultureIgnoreCase)))
                    .Select(x => new Tuple<ObjectViewResponse, ObjectViewResponse>(x, null));

                objects.AddRange(objectsToAdd);
            }

            return objects;
        }

        private async Task AddToRepository(Tuple<ObjectViewResponse, ObjectViewResponse> currentLevelObject)
        {
            var parent = currentLevelObject.Item2;
            var current = currentLevelObject.Item1;

            var newFile = new FileEntity
            {
                Crc32 = current.Crc32,
                Name = current.Name,
                IsFolder = current.IsFolder,
                Token = AccountObject.Token,
                Size = current.Size,
                UploadId = current.UploadId
            };

            if (parent != null)
            {
                newFile.Parent = await FileRepository.FindAsync(AccountObject.Token, parent.UploadId);
            }

            var file = await FileRepository.FindAsync(newFile);

            if (file != null)
            {
                if (file.UploadId == newFile.UploadId)
                {
                    //the same file scanned twice, should never happened, but who knows
                    file.Status = FileStatus.Conflict;
                }
                else
                {
                    newFile.Name = await GetIncrementName(newFile);
                    newFile.Status = FileStatus.RenameRequired;
                    await FileRepository.AddAsync(newFile);
                }
            }
            else
            {
                newFile.Status = FileStatus.RemotelyCreated;
                await FileRepository.AddAsync(newFile);
            }
        }
    }
}
