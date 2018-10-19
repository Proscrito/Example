using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;

namespace FexSync.Data.FileCommands.LocalCommands
{
    /// <summary>
    /// Command accepts the set of files and creates entities in the database for the further synchronization
    /// If file
    /// </summary>
    public class LocalFileBulkCreatedCommand : LocalFileBulkCommandBase
    {
        public delegate LocalFileBulkCreatedCommand Factory(List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject);

        private static readonly IList<string> Buffer = new List<string>();

        public LocalFileBulkCreatedCommand(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject)
            : base(repository, fileSystemEventArgs, accountObject)
        {
        }

        public override bool CanExecute()
        {
            return FileSystemEventArgs != null && FileSystemEventArgs.Any();
        }

        protected override async Task ExecuteInternal()
        {
            await CreateFiles();
        }

        private async Task CreateFiles()
        {
            var relativePathList = GetFilteredRelatedPathList();
            //include not processed files
            relativePathList.AddRange(Buffer);

            if (!relativePathList.Any())
            {
                //nothing to process after filtering
                return;
            }

            var maxCounter = relativePathList.Select(x => x.Split(Path.DirectorySeparatorChar).Length).Max();
            var minCounter = relativePathList.Select(x => x.Split(Path.DirectorySeparatorChar).Length).Min();

            for (var i = minCounter; i <= maxCounter; i++)
            {
                await CreateFilesOneLevel(relativePathList, i);
            }

            await FileRepository.SaveAsync();
        }

        private async Task CreateFilesOneLevel(IEnumerable<string> relativePathList, int level)
        {
            foreach (var relativePath in relativePathList.Where(x =>
                x.Split(Path.DirectorySeparatorChar).Length == level))
            {
                var file = await FileRepository.FindAsync(AccountObject.Token, relativePath);
                var fullName = Path.Combine(AccountObject.Path, relativePath);
                var fileInfo = GetFileInfo(fullName);

                //file already exists
                if (file != null)
                {
                    //file exists on the server
                    if (file.UploadId.HasValue)
                    {
                        //file was changed when the sync was not running
                        if (Conflicted(file, fileInfo))
                        {
                            await FileRepository.UpdateBranchStatusAsync(file, FileStatus.Conflict);
                        }
                        else
                        {
                            //no conflicts, already synchronized, update
                            file.LastWrite = fileInfo.LastWriteTime.Ticks;
                            file.Status = FileStatus.Synchronized;
                        }
                    }
                    else
                    {
                        //it is new record, ensure it has correct status
                        file.Status = FileStatus.LocallyCreated;
                    }

                    continue;
                }

                file = new FileEntity
                {
                    Token = AccountObject.Token,
                    Name = Path.GetFileName(relativePath),
                    Status = FileStatus.LocallyCreated,
                    Hash = relativePath,
                    LastWrite = fileInfo.LastWriteTime.Ticks,
                    IsFolder = fileInfo.Attributes.HasFlag(FileAttributes.Directory)
                };

                //not root
                if (level > 1)
                {
                    var parentRelativePath = Path.GetDirectoryName(relativePath);
                    var parent = await FileRepository.FindAsync(AccountObject.Token, parentRelativePath);

                    if (parent == null)
                    {
                        //no parent in DB, let's try next time
                        AddToBuffer(relativePath);
                        continue;
                    }

                    if (Buffer.Contains(relativePath))
                    {
                        Buffer.Remove(relativePath);
                    }

                    file.Parent = parent;
                }

                await FileRepository.AddAsync(file);
            }
        }

        private bool Conflicted(FileEntity file, FileSystemInfo fileInfo)
        {
            var typeMismatch = file.IsFolder && !fileInfo.Attributes.HasFlag(FileAttributes.Directory)
                || !file.IsFolder && fileInfo.Attributes.HasFlag(FileAttributes.Directory);

            if (typeMismatch)
            {
                return true;
            }

            if (!fileInfo.Attributes.HasFlag(FileAttributes.Directory))
            {
                if (!file.LastWrite.HasValue && file.Status == FileStatus.RemotelyCreated)
                {
                    return false;
                }

                if (((FileInfo)fileInfo).Length != file.Size)
                {
                    return true;
                }

                if (fileInfo.LastWriteTime.Ticks > file.LastWrite)
                {
                    return true;
                }
            }

            return false;
        }

        private FileSystemInfo GetFileInfo(string fullName)
        {
            if (File.Exists(fullName))
            {
                return new FileInfo(fullName);
            }

            return new DirectoryInfo(fullName);
        }

        //if rhere is no parent directory in the database, save the path to buffer and try later
        private void AddToBuffer(string relativePath)
        {
            if (!Buffer.Contains(relativePath))
            {
                Buffer.Add(relativePath);
            }
        }
    }
}
