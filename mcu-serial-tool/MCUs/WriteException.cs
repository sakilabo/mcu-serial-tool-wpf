using System;

namespace MCUSerialTool
{
    [Flags]
    internal enum WriteFailureActions
    {
        None = 0,
        ClearMCU = 1
    }
    internal class WriteException : Exception
    {
        public WriteException(string message, WriteFailureActions failureActions) : base(message)
        {
            FailureActions = failureActions;
        }
        public WriteFailureActions FailureActions { get; private set; }
    }
}
