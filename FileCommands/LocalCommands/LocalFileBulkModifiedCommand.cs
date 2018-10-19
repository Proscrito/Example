using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;
using FexSync.Data.Repository.Database.Entity;
using FexSync.Data.Repository.Database.Entity.Types;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public class LocalFileBulkModifiedCommand : LocalFileBulkCommandBase
    {
        public delegate LocalFileBulkModifiedCommand Factory(List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject);

        public LocalFileBulkModifiedCommand(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject)
            : base(repository, fileSystemEventArgs, accountObject)
        {
        }

        protected override async Task ExecuteInternal()
        {
            var relativePathList = GetFilteredRelatedPathList();

            foreach (var relativePath in relativePathList)
            {
                var file = await FileRepository.FindAsync(AccountObject.Token, relativePath);

                if (await NeedToModify(file))
                {
                    file.Status = FileStatus.LocallyModified;
                    file.LastWrite = File.GetLastWriteTime(Path.Combine(AccountObject.Path, relativePath)).Ticks;
                }
            }

            await FileRepository.SaveAsync();
        }

        private async Task<bool> NeedToModify(FileEntity file)
        {
            //no folders should be here, but who knows about this f@kin FileSystemWatcher
            if (file == null || file.IsFolder)
            {
                //no file to modify
                return false;
            }

            if (file.Status == FileStatus.Synchronized && file.LastSaved.AddSeconds(3) >= DateTime.Now)
            {
                //was recently syncronized
                return false;
            }

            var fileInfo = (FileInfo)await GetFileInfo(file);

            if (fileInfo.Length == file.Size && fileInfo.LastWriteTime.Ticks <= file.LastWrite)
            {
                //wasn't really changed
                return false;
            }

            //modify only syncronized files
            return file.Status == FileStatus.Synchronized;
        }
    }
}
