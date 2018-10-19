using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;

namespace FexSync.Data.FileCommands
{
    public abstract class FileCommandBase : IFileCommand
    {
        protected IFileRepository FileRepository { get; }

        protected CancellationToken _cancellationToken = CancellationToken.None;

        protected SynchronizationObject AccountObject { get; }

        protected FileCommandBase(IFileRepository repository, SynchronizationObject accountObject)
        {
            FileRepository = repository;
            AccountObject = accountObject;
        }

        public event Action<FileCommandStatus> OnStatusChanged;

        public virtual bool CanExecute()
        {
            return true;
        }

        public virtual async Task Execute()
        {
            await Execute(CancellationToken.None);
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            Logger.WriteLine($"Executing command: {GetType()}");
            _cancellationToken = cancellationToken;

            try
            {
                await ExecuteInternal();
            }
            catch (Exception e)
            {
                throw new CommandFatalException(GetType(), e);
            }

            Logger.WriteLine($"Executing command: {GetType()} finished in {TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds):g}");
        }

        protected abstract Task ExecuteInternal();

        public virtual void Dispose()
        {
            FileRepository?.Dispose();
        }

        protected virtual void ChangeStatus(FileCommandStatus status)
        {
            //fire event in another thread to not hold the command processing
            Task.Run(() => OnStatusChanged?.Invoke(status));
        }

        protected virtual async Task<string> GetIncrementName(FileEntity fileEntity)
        {
            var files = await FileRepository.FindAllAsync(x => x.Token == fileEntity.Token && x.ParentId == fileEntity.ParentId);

            if (!files.Any())
            {
                return fileEntity.Name;
            }

            var filename = Path.GetFileNameWithoutExtension(fileEntity.Name);
            var extension = Path.GetExtension(fileEntity.Name);
            var pattern = $"^{filename}(\\((\\d+)\\))?$";

            var matches = files
                .Select(x => Regex.Match(x.Name, pattern))
                .Where(x => x.Success)
                .ToList();

            if (!matches.Any())
            {
                return fileEntity.Name;
            }

            var number = matches
                .Select(x => x.Groups[2].Length == 0 ? 0 : Convert.ToInt32(x.Groups[2].Value))
                .Max();

            var newName = $"{filename}({++number})";

            return string.IsNullOrEmpty(extension) ? newName : $"{newName}.{extension}";
        }

        protected async Task<FileSystemInfo> GetFileInfo(FileEntity file)
        {
            var relativePath = await FileRepository.GetPathAsync(file.Id);
            var fullPath = Path.Combine(AccountObject.Path, relativePath);

            if (File.Exists(fullPath))
            {
                return new FileInfo(fullPath);
            }

            return new DirectoryInfo(fullPath);
        }

        protected async Task Synchronize(FileEntity file)
        {
            var fileInfo = await GetFileInfo(file);
            file.LastWrite = fileInfo.LastWriteTime.Ticks;
            file.Status = FileStatus.Synchronized;
        }
    }
}
