namespace MCUSerialTool
{
    internal sealed class WriteResult
    {
        private WriteResult(
            bool succeeded,
            string? errorMessage,
            WriteFailureActions failureActions)
        {
            Succeeded = succeeded;
            ErrorMessage = errorMessage;
            FailureActions = failureActions;
        }
        public bool Succeeded { get; private set; }
        public string? ErrorMessage { get; private set; }
        public WriteFailureActions FailureActions { get; private set; }

        public static WriteResult Success()
        {
            return new WriteResult(true, null, WriteFailureActions.None);
        }
        public static WriteResult Failure(string errorMessage, WriteFailureActions failureActions)
        {
            return new WriteResult(false, errorMessage, failureActions);
        }
    }
}
