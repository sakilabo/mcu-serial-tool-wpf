using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCUSerialTool
{
    internal partial class Connection : INotifyPropertyChanged
    {
        public const int DefaultBaudRate = 115200;

        private static readonly IReadOnlyList<int> BaudRateValues = Array.AsReadOnly(
        [
            4800,
            9600,
            19200,
            38400,
            57600,
            115200,
            230400
        ]);

        private IReadOnlyList<Type> _mcuTypes;
        private int _baudRate = DefaultBaudRate;
        private bool _isWriting;
        private bool _isWriteProgressIndeterminate;
        private MCU? _mcu;
        protected ConnectionMode _mode;
        private SerialPort? _serialPort;
        private double _writeProgress;

        public Connection(
            string port,
            string name)
            : this(
                port,
                name,
                Array.AsReadOnly(new Type[0])) { }

        protected Connection(
            string port,
            string name,
            IReadOnlyList<Type> mcuTypes)
        {
            if (string.IsNullOrWhiteSpace(port))
            {
                throw new ArgumentException("ポート名が指定されていません。", "port");
            }
            Port = port;
            Name = string.IsNullOrWhiteSpace(name) ? port : name;
            _mcuTypes = mcuTypes
                ?? Array.AsReadOnly(new Type[0]);
        }

        public string Port { get; private set; }

        public string Name { get; private set; }

        public virtual ConnectionType ConnectionType
        {
            get { return ConnectionType.SerialPort; }
        }

        public virtual string ResetConfirmationMessage
        {
            get
            {
                return "DTR 信号によるマイコンのリセットを試行します。よろしいですか？";
            }
        }

        public virtual bool IsSupported
        {
            get { return false; }
        }

        public MCU? MCU
        {
            get { return _mcu; }
            protected set
            {
                if (ReferenceEquals(_mcu, value)) { return; }
                _mcu = value;
                OnPropertyChanged("MCU");
                OnPropertyChanged("MCUDisplay");
            }
        }

        public bool IsWriting
        {
            get { return _isWriting; }
            private set
            {
                if (_isWriting == value) { return; }
                _isWriting = value;
                OnPropertyChanged("IsWriting");
            }
        }

        public bool IsWriteProgressIndeterminate
        {
            get { return _isWriteProgressIndeterminate; }
            private set
            {
                if (_isWriteProgressIndeterminate == value) { return; }
                _isWriteProgressIndeterminate = value;
                OnPropertyChanged("IsWriteProgressIndeterminate");
            }
        }

        public double WriteProgress
        {
            get { return _writeProgress; }
            private set
            {
                if (_writeProgress == value) { return; }
                _writeProgress = value;
                OnPropertyChanged("WriteProgress");
            }
        }

        public IReadOnlyList<Type> MCUTypes
        {
            get { return _mcuTypes; }
            protected set
            {
                _mcuTypes = value
                    ?? Array.AsReadOnly(new Type[0]);
                OnPropertyChanged("MCUTypes");
                OnPropertyChanged("CanSelectMCU");
            }
        }

        public bool CanSelectMCU
        {
            get { return MCUTypes.Count > 1; }
        }

        public virtual string MCUDisplay
        {
            get
            {
                if (!IsSupported) { return "非対応"; }
                return string.Empty;
            }
        }

        internal bool SupportsFirmware(Firmware firmware)
        {
            return GetMCUType(firmware) != null;
        }

        internal virtual Type? GetMCUType(Firmware firmware)
        {
            return null;
        }

        public virtual ConnectionMode Mode
        {
            get
            {
                lock (this)
                {
                    return _mode;
                }
            }
            internal set
            {
                lock (this)
                {
                    var currentMode = _mode;
                    if (currentMode == value) { return; }
                    switch (value)
                    {
                        case ConnectionMode.Offline:
                            Disconnect();
                            break;
                        case ConnectionMode.SerialMonitor:
                            Connect();
                            break;
                        default:
                            throw new InvalidOperationException("不正なモードです: " + value);
                    }
                _mode = value;
                }
                OnModeChanged();
            }
        }

        public virtual int BaudRate
        {
            get { return _baudRate; }
            internal set
            {
                if (_baudRate == value) { return; }

                _baudRate = value;
                OnPropertyChanged("BaudRate");
            }
        }

        public static IReadOnlyList<int> SupportedBaudRates
        {
            get { return BaudRateValues; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<ConnectionDataReceivedEventArgs>? DataReceived;

        public event EventHandler? Disconnected;

        protected void OnModeChanged()
        {
            OnPropertyChanged("Mode");
            if (Mode == ConnectionMode.Offline)
            {
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        protected virtual void Connect()
        {
            if (_serialPort != null) { return; }
            var serialPort = new SerialPort(Port, BaudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = new UTF8Encoding(false),
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false
            };
            try
            {
                serialPort.Open();
                _serialPort = serialPort;
                serialPort.DataReceived += SerialPort_DataReceived;
            }
            catch
            {
                _serialPort = null;
                serialPort.DataReceived -= SerialPort_DataReceived;
                try { serialPort.Close(); } catch { /* Ignore errors. */ }
                try { serialPort.Dispose(); } catch { /* Ignore errors. */ }
                throw;
            }
        }

        protected virtual void Disconnect()
        {
            var serialPort = _serialPort;
            if (serialPort == null) { return; }
            serialPort.DataReceived -= SerialPort_DataReceived;
            _serialPort = null;
            try { serialPort.Close(); } catch { /* Ignore errors. */ }
            try { serialPort.Dispose(); } catch { /* Ignore errors. */ }
        }

        internal void SelectMCU(Type mcuType)
        {
            if (mcuType == null
                || !MCUTypes.Contains(mcuType))
            {
                throw new InvalidOperationException(
                    "選択できない MCU です。");
            }

            var constructor = mcuType.GetConstructor([GetType()]);
            if (constructor == null
                || !typeof(MCU).IsAssignableFrom(mcuType))
            {
                throw new InvalidOperationException(
                    "MCUを生成できません: " + mcuType);
            }
            MCU = (MCU)constructor.Invoke([this]);
        }

        internal async Task<WriteResult> WriteFirmwareAsync(
            Firmware firmware,
            bool verify,
            Action<string> messageCallback)
        {
            var mcu = MCU;
            if (mcu == null) { throw new InvalidOperationException("MCU が特定されていません。"); }

            if (firmware == null) { throw new ArgumentNullException("firmware"); }

            if (IsWriting) { throw new InvalidOperationException("書き込み中です。"); }

            if (messageCallback == null) { throw new ArgumentNullException("messageCallback"); }

            IsWriting = true;
            IsWriteProgressIndeterminate = true;
            WriteProgress = 0;
            var writeProgress = new Progress<double>(progress =>
            {
                WriteProgress = progress;
                IsWriteProgressIndeterminate = false;
            });

            try
            {
                var result = await Task.Run(() => mcu.WriteFirmware(
                    firmware,
                    verify,
                    messageCallback,
                    writeProgress));
                if (!result.Succeeded
                    && CanSelectMCU
                    && (result.FailureActions & WriteFailureActions.ClearMCU) != 0)
                {
                    MCU = null;
                }

                return result;
            }
            finally
            {
                IsWriteProgressIndeterminate = false;
                WriteProgress = 0;
                IsWriting = false;
            }
        }

        internal bool SupportsBaudRate(int baudRate)
        {
            return SupportedBaudRates.Contains(baudRate);
        }

        internal virtual void ResetMCU()
        {
            var serialPort = _serialPort;
            if (serialPort == null || !serialPort.IsOpen) { return; }

            try
            {
                serialPort.DtrEnable = false;
                Thread.Sleep(50);
                serialPort.DtrEnable = true;
                Thread.Sleep(100);
                serialPort.DtrEnable = false;
            }
            catch (Exception ex) when (
                ex is IOException
                || ex is InvalidOperationException
                || ex is UnauthorizedAccessException)
            {
                throw new DeviceConnectionException(
                    "MCU リセットに失敗しました: " + ex.Message);
            }
        }

        internal virtual void SendSerialMonitorText(string text)
        {
            if (text == null) { throw new ArgumentNullException("text"); }

            lock (this)
            {
                if (Mode != ConnectionMode.SerialMonitor)
                {
                    throw new InvalidOperationException(
                        "シリアルモニターモードではありません。");
                }

                var serialPort = _serialPort;
                if (serialPort == null || !serialPort.IsOpen)
                {
                    throw new DeviceConnectionException("接続が開かれていません。");
                }

                try
                {
                    serialPort.Write(text);
                }
                catch (Exception ex) when (
                    ex is IOException
                    || ex is InvalidOperationException
                    || ex is TimeoutException)
                {
                    throw new DeviceConnectionException(
                        "シリアルポートへの送信に失敗しました: " + ex.Message);
                }
            }
        }

        protected void RaiseDataReceived(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }

            DataReceived?.Invoke(this, new ConnectionDataReceivedEventArgs(text));
        }

        private void SerialPort_DataReceived(
            object sender,
            SerialDataReceivedEventArgs e)
        {
            var serialPort = sender as SerialPort;
            if (serialPort == null || !ReferenceEquals(serialPort, _serialPort)) { return; }

            try { RaiseDataReceived(serialPort.ReadExisting()); } catch (InvalidOperationException) { /* Ignore errors. */ } catch (IOException) { /* Ignore errors. */ }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
