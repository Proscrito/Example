using System;

namespace FexSync.Data.FileCommands
{
    /// <summary>
    /// Not a showstopper for the command, command can be queued again
    /// </summary>
    public class CommandSafeException : InvalidOperationException
    {
        public const string DefaultSafeMessage = "Command execution failed. See inner exception for the details. Command will be requeued.";
        public Type CommandType { get; set; }

        public CommandSafeException(string message, Type commandType, Exception innerException) 
            : base(message, innerException)
        {
            CommandType = commandType;
        }

        public CommandSafeException(Type commandType, Exception innerException)
            : base(DefaultSafeMessage, innerException)
        {
            CommandType = commandType;
        }
    }
}
