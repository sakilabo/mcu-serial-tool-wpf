namespace MCUSerialTool
{
    internal sealed class DeviceConnectionException : WriteException
    {
        public DeviceConnectionException(string message)
            : base(message, WriteFailureActions.ClearMCU) { }
    }
}
