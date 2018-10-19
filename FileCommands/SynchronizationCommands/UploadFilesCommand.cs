using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;
using FexSync.Data.Utility;
using Net.Fex.Api;

namespace FexSync.Data.FileCommands.SynchronizationCommands
{
    public class UploadFilesCommand : FileCommandConnectionBase
    {
        public delegate UploadFilesCommand Factory(SynchronizationObject accountObject);

        private List<FileEntity> _files;

        public UploadFilesCommand(IFileRepository repository, IConnection connection, SynchronizationObject accountObject)
            : base(repository, connection, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            _files = await FileRepository.FindAllAsync(x => x.Token == AccountObject.Token && x.Status == FileStatus.LocallyCreated);
            var fileInfoList = _files.Select(CreateFileInfo).ToList();
            var folders = fileInfoList.OfType<DirectoryInfo>();
            var files = fileInfoList.OfType<FileInfo>();

            try
            {
                await CreateFolders(folders);
                await UploadFiles(files);
            }
            finally
            {
                await FileRepository.SaveAsync();
            }
        }

        private async Task UploadFiles(IEnumerable<FileInfo> files)
        {
            await OptimizationChain(files);
        }

        /// <summary>
        /// The fastest at the moment
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        private async Task OptimizationChain(IEnumerable<FileInfo> files)
        {
            var filesQuery = files.OrderBy(x => x.FullName.Split(Path.DirectorySeparatorChar).Length).ToList();
            var tasks = new List<Task>();

            foreach (var fileInfo in filesQuery)
            {
                var task = Task.Run(async () =>
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var file = _files.First(x => x.Hash == fileInfo.FullName);
                    var response = await Connection.UploadAsync(AccountObject.Token, file.Parent?.UploadId, fileInfo.FullName);
                    file.Crc32 = response.Crc32;
                    file.UploadId = response.UploadId;
                    file.Size = response.Size;
                    file.Status = FileStatus.Synchronized;
                });

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            LogUploadedFiles(filesQuery);
        }

        private void LogUploadedFiles(List<FileInfo> filesQuery)
        {
            Logger.WriteLine($"Uploaded totally {filesQuery.Sum(x => x.Length).ToBytesFormat()} in {filesQuery.Count} files");
        }

        private async Task CreateFolders(IEnumerable<DirectoryInfo> folders)
        {
            var foldersQuery = folders.OrderBy(x => x.FullName.Split(Path.DirectorySeparatorChar).Length);

            foreach (var directoryInfo in foldersQuery)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var file = _files.First(x => x.Hash == directoryInfo.FullName);
                var response = await Connection.CreateFolderAsync(AccountObject.Token, file.Parent?.UploadId, file.Name);
                file.Status = FileStatus.Synchronized;
                file.UploadId = response;
                file.IsFolder = true;
            }
        }

        private FileSystemInfo CreateFileInfo(FileEntity file)
        {
            var path = FileRepository.GetPath(file.Id);
            var fullPath = Path.Combine(AccountObject.Path, path);
            file.Hash = fullPath;

            if (Directory.Exists(fullPath))
            {
                return new DirectoryInfo(fullPath);
            }

            return new FileInfo(fullPath);
        }
    }
}
