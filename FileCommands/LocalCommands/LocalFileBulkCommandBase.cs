using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FexSync.Data.Configuration.DatabaseConfiguration.Model;
using FexSync.Data.Repository;

namespace FexSync.Data.FileCommands.LocalCommands
{
    public abstract class LocalFileBulkCommandBase : FileCommandBase
    {
        protected List<FileSystemEventArgs> FileSystemEventArgs { get; set; }

        protected LocalFileBulkCommandBase(IFileRepository repository, List<FileSystemEventArgs> fileSystemEventArgs, SynchronizationObject accountObject)
            : base(repository, accountObject)
        {
            FileSystemEventArgs = fileSystemEventArgs;
        }

        public override async Task Execute()
        {
            Validate();

            await base.Execute();
        }

        protected virtual List<string> GetFilteredRelatedPathList()
        {
            return FileSystemEventArgs
                .Where(IgnoredFileAttributesExcludePredicate)
                .Select(RelativePathSelector)
                .Where(IgnoredFoldersExcludePredicate)
                .ToList();
        }

        public static FileAttributes IgnoredAttributes
        {
            get
            {
                var ret = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | FileAttributes.Temporary;
                return ret;
            }
        }

        private bool IgnoredFileAttributesExcludePredicate(FileSystemEventArgs arg)
        {
            if (File.Exists(arg.FullPath))
            {
                var fileInfo = new FileInfo(arg.FullPath);

                var hasIgnoredAttributes = ((ulong)(fileInfo.Attributes & IgnoredAttributes) > 0);
#if DEBUG
                var ignored = fileInfo.Attributes.HasFlag(FileAttributes.Hidden)
                              || fileInfo.Attributes.HasFlag(FileAttributes.System)
                              || fileInfo.Attributes.HasFlag(FileAttributes.Offline)
                              || fileInfo.Attributes.HasFlag(FileAttributes.Temporary);

                System.Diagnostics.Logger.Assert(ignored == hasIgnoredAttributes);
#endif
                return !hasIgnoredAttributes;
            }

            return true;
        }

        private bool IgnoredFoldersExcludePredicate(string relativePath)
        {
            return !AccountObject.IgnoredFolders.Any(y => relativePath.StartsWith(y, StringComparison.InvariantCultureIgnoreCase));
        }

        private string RelativePathSelector(FileSystemEventArgs arg)
        {
            return arg.FullPath.Replace(AccountObject.Path, "").Trim(Path.DirectorySeparatorChar);
        }

        protected virtual void Validate()
        {
            if (AccountObject == null)
            {
                throw new CommandFatalException("AccountObject required", GetType(), null);
            }
        }
    }
}
