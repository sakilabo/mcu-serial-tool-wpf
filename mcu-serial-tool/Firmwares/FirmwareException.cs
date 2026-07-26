using System;

namespace MCUSerialTool
{
    internal class FirmwareException : Exception
    {
        protected FirmwareException(string message) : base(message) { }

        protected FirmwareException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    internal sealed class FirmwareReadException : FirmwareException
    {
        public FirmwareReadException(string message, Exception innerException)
            : base("ファームウェアを読み込めません: " + message, innerException) { }
    }

    internal sealed class FirmwareValidationException : FirmwareException
    {
        public FirmwareValidationException(string inspectionItem)
            : base(".bin ファイル検証エラー [" + inspectionItem + "]") { }
    }

    internal sealed class FirmwareCompatibilityException : FirmwareException
    {
        public FirmwareCompatibilityException(string message) : base(message) { }
    }
}
