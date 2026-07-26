using System;

namespace MCUSerialTool
{
    internal abstract class MCU
    {
        private Action<string>? _messageCallback;
        private IProgress<double>? _writeProgress;

        protected MCU(Connection connection)
        {
            if (connection == null) { throw new ArgumentNullException("connection"); }
            Connection = connection;
        }

        public Connection Connection { get; private set; }

        internal WriteResult WriteFirmware(
            Firmware firmware,
            bool verify,
            Action<string> messageCallback,
            IProgress<double> writeProgress)
        {
            if (firmware == null) { throw new ArgumentNullException("firmware"); }
            if (messageCallback == null) { throw new ArgumentNullException("messageCallback"); }
            if (writeProgress == null) { throw new ArgumentNullException("writeProgress"); }
            _messageCallback = messageCallback;
            _writeProgress = writeProgress;
            try
            {
                firmware.Reload();
                if (!SupportsFirmware(firmware.Type))
                {
                    throw new FirmwareCompatibilityException(
                        "ファームウェアの種別が MCU と一致しません。");
                }

                WriteFirmware(firmware, verify);
                return WriteResult.Success();
            }
            catch (FirmwareException ex)
            {
                return WriteResult.Failure(ex.Message, WriteFailureActions.None);
            }
            catch (WriteException ex)
            {
                return WriteResult.Failure(ex.Message, ex.FailureActions);
            }
            catch (Exception ex)
            {
                return WriteResult.Failure(ex.Message, WriteFailureActions.ClearMCU);
            }
            finally
            {
                _messageCallback = null;
                _writeProgress = null;
            }
        }

        protected abstract bool SupportsFirmware(
            FirmwareType firmwareType);

        protected abstract void WriteFirmware(Firmware firmware, bool verify);

        protected void ReportMessage(string message)
        {
            if (_messageCallback != null) { _messageCallback(message); }
        }

        protected void ReportProgress(long completedBytes, long totalBytes)
        {
            var progress = totalBytes <= 0 ? 0 : Math.Min(1.0, (double)completedBytes / totalBytes);
            if (_writeProgress != null) { _writeProgress.Report(progress); }
        }
    }
}
