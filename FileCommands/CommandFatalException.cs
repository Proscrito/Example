using System;

namespace FexSync.Data.FileCommands
{
    /// <summary>
    /// Showstopper, command can never run
    /// </summary>
    public class CommandFatalException : CommandSafeException
    {
        public const string DefaultFatalMessage = "FATAL ERROR. Command execution failed. See inner exception for the details. Command will be eliminated.";

        public CommandFatalException(string message, Type commandType, Exception innerException) 
            : base(message, commandType, innerException)
        {
        }

        public CommandFatalException(Type commandType, Exception innerException) 
            : base(DefaultFatalMessage, commandType, innerException)
        {
        }
    }
}
