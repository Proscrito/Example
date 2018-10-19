using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using Net.Fex.Api;
using System.IO.Compression;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class DownloadFilesCommand : FileCommandConnectionBase
    {
        public delegate DownloadFilesCommand Factory(SynchronizationObject accountObject);

        private const long TooBigSize = 100 * 1024 * 1024; //100 megabytes

        public DownloadFilesCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject)
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            var files = await FileRepository.FindAllAsync(x => x.Token == AccountObject.Token && x.Status == FileStatus.RemotelyCreated);

            try
            {
                await CreateFolders(files);
                await DownloadContent(files);
            }
            finally
            {
                await FileRepository.SaveAsync();
            }
        }

        private async Task DownloadContent(IReadOnlyCollection<FileEntity> files)
        {
            var folders = files.Where(x => x.IsFolder).ToList();
            var tasks = new List<Task>();

            var rootContent = files.Where(x => !x.ParentId.HasValue && !x.IsFolder).ToList();
            DownloadFolderContent(rootContent, tasks, null);

            foreach (var folder in folders)
            {
                var content = files.Where(x => x.ParentId == folder.Id && !x.IsFolder).ToList();
                DownloadFolderContent(content, tasks, folder);
            }

            await Task.WhenAll(tasks);
        }

        private void DownloadFolderContent(IReadOnlyCollection<FileEntity> content, List<Task> tasks, FileEntity folder)
        {
            if (!content.Any())
            {
                return;
            }

            var singleContent = content.Where(x => x.Size >= TooBigSize).ToList();
            var bulkContent = content.Except(singleContent).ToList();

            tasks.AddRange(singleContent.Select(DownloadSingle));

            if (bulkContent.Count == 1)
            {
                tasks.Add(DownloadSingle(bulkContent.First()));
            }

            if (bulkContent.Count > 1)
            {
                tasks.Add(DownloadBulk(folder, bulkContent));
            }
        }

        private async Task DownloadBulk(FileEntity folder, IList<FileEntity> bulkContent)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var tempDirName = Guid.NewGuid().ToString("N");
            var tempDir = Path.Combine(Path.GetTempPath(), tempDirName);
            var tempFile = Path.Combine(tempDir, "download.zip");

            try
            {
                Directory.CreateDirectory(tempDir);
            }
            catch (IOException e)
            {
                throw new CommandFatalException(GetType(), e);
            }

            long expectedLength = bulkContent
                .Where(fileEntity => fileEntity.UploadId.HasValue)
                .Select(fileEntity => fileEntity.Size).Sum();

            var idList = bulkContent
                .Where(x => x.UploadId.HasValue)
                .Select(x => x.UploadId.Value);

            await Connection.GetBulkAsync(AccountObject.Token, folder?.UploadId, tempFile, idList, expectedLength);

            await CopyToTargetDir(tempFile, folder?.Id);

            foreach (var fileEntity in bulkContent)
            {
                await Synchronize(fileEntity);
            }
        }

        private async Task CopyToTargetDir(string tempFile, long? currentLevelId)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var folderPath = AccountObject.Path;

            if (currentLevelId.HasValue)
            {
                var folder = await FileRepository.GetPathAsync(currentLevelId.Value);
                folderPath = Path.Combine(folderPath, folder);
            }

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            ZipFile.ExtractToDirectory(tempFile, folderPath);

            try
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                Directory.Delete(Path.GetDirectoryName(tempFile), true);
            }
            catch (IOException)
            {
                //supress
            }
        }

        private async Task DownloadSingle(FileEntity fileEntity)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var relativePath = await FileRepository.GetPathAsync(fileEntity.Id);
            var fullPath = Path.Combine(AccountObject.Path, relativePath);

            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch (IOException)
                {
                    fileEntity.Status = FileStatus.Conflict;
                    return;
                }
            }

            await Connection.GetAsync(AccountObject.Token, fileEntity.UploadId, fullPath, fileEntity.Size);
            await Synchronize(fileEntity);
        }

        private async Task CreateFolders(IEnumerable<FileEntity> files)
        {
            var folders = files.Where(x => x.IsFolder).ToList();
            var currentLevelFolders = folders.Where(x => x.ParentId == null || folders.All(y => y.Id != x.ParentId)).ToList();

            while (currentLevelFolders.Any())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var buffer = new List<FileEntity>();

                foreach (var currentLevelFolder in currentLevelFolders)
                {
                    var relativePath = await FileRepository.GetPathAsync(currentLevelFolder.Id);
                    var fullPath = Path.Combine(AccountObject.Path, relativePath);

                    if (!Directory.Exists(fullPath))
                    {
                        Directory.CreateDirectory(fullPath);
                    }

                    currentLevelFolder.Status = FileStatus.Synchronized;
                    var children = folders.Where(x => x.ParentId == currentLevelFolder.Id);
                    buffer.AddRange(children);
                }

                currentLevelFolders = buffer;
            }
        }
    }
}
