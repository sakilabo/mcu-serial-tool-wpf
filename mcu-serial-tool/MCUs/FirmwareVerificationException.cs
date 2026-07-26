namespace MCUSerialTool
{
    internal sealed class FirmwareVerificationException : WriteException
    {
        public FirmwareVerificationException(string message)
            : base(message, WriteFailureActions.None) { }
    }
}
