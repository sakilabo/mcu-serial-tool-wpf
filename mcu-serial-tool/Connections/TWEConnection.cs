using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCUSerialTool
{
    internal sealed class TWEConnection : Connection
    {
        private readonly string? _serialNumber; // FTDI のシリアル番号 (FTDI デバイスへの接続に使用する)
        private FtdiDevice? _device;
        private CancellationTokenSource? _receiveCancellation;
        private Task? _receiveTask;

        internal TWEConnection(
            string port,
            string name,
            string? serialNumber,
            Type[] mcuTypes)
            : base(port, name)
        {
            _serialNumber = serialNumber;

            if (mcuTypes == null
                || mcuTypes.Length == 0)
            {
                throw new ArgumentException(
                    "MCU クラスが指定されていません。",
                    "mcuTypes");
            }

            foreach (var mcuType in mcuTypes)
            {
                var isSupportedType = mcuType != null
                    && typeof(TWEMCU).IsAssignableFrom(mcuType)
                    && mcuType.GetConstructor(
                        [typeof(TWEConnection)]) != null;
                if (!isSupportedType)
                {
                    throw new ArgumentException(
                        "未対応の MCU クラスです: " + mcuType,
                        "mcuTypes");
                }
            }

            MCUTypes = Array.AsReadOnly(
                (Type[])mcuTypes.Clone());
            if (MCUTypes.Count == 1)
            {
                SelectMCU(MCUTypes[0]);
            }
        }

        public override bool IsSupported
        {
            get { return true; }
        }

        public override ConnectionType ConnectionType
        {
            get { return MCUSerialTool.ConnectionType.TWELITE; }
        }

        public override string ResetConfirmationMessage
        {
            get { return "TWELITE を再起動します。よろしいですか？"; }
        }

        public override string MCUDisplay
        {
            get
            {
                var models = new List<string>();
                if (MCUTypes.Contains(typeof(TWE516xMCU)))
                {
                    models.Add("BLUE");
                    models.Add("RED");
                }
                if (MCUTypes.Contains(typeof(TWE518xMCU)))
                {
                    models.Add("GOLD");
                }
                return models.Count == 0
                    ? string.Empty
                    : "TWELITE " + string.Join(" / ", models);
            }
        }

        internal override Type? GetMCUType(Firmware firmware)
        {
            if (firmware == null) { return null; }

            Type mcuType;
            switch (firmware.Type)
            {
                case FirmwareType.TWELITE_BLUE:
                case FirmwareType.TWELITE_RED:
                    mcuType = typeof(TWE516xMCU);
                    break;
                case FirmwareType.TWELITE_GOLD:
                    mcuType = typeof(TWE518xMCU);
                    break;
                default:
                    return null;
            }

            return MCUTypes.Contains(mcuType)
                ? mcuType
                : null;
        }

        public override int BaudRate
        {
            get { return base.BaudRate; }
            internal set
            {
                lock (this)
                {
                    _device?.BaudRate = (uint)value;
                    base.BaudRate = value;
                }
            }
        }

        public override ConnectionMode Mode
        {
            get { return base.Mode; }
            internal set
            {
                lock (this)
                {
                    var currentMode = _mode;
                    if (currentMode == value) { return; }

                    if (value == ConnectionMode.Offline)
                    {
                        Disconnect();
                    }
                    else if (currentMode == ConnectionMode.Offline
                        && value == ConnectionMode.Programming)
                    {
                        ConnectForProgramming();
                    }
                    else if (currentMode == ConnectionMode.Offline
                        && value == ConnectionMode.SerialMonitor)
                    {
                        Connect();
                    }
                    else if (currentMode == ConnectionMode.Programming
                        && value == ConnectionMode.SerialMonitor)
                    {
                        SwitchToSerialMonitor();
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            currentMode + " から " + value + " へ変更できません。");
                    }
                    _mode = value;
                }

                OnModeChanged();
            }
        }

        protected override void Connect()
        {
            lock (this)
            {
                if (_device != null) { throw new InvalidOperationException("既に接続されています。"); }
                if (_serialNumber == null) { throw new ArgumentNullException("serialNumber"); }
                FtdiDevice device = FtdiDevice.Open(_serialNumber);
                try
                {
                    device.ConfigureUart();
                    device.BaudRate = (uint)BaudRate;
                    _device = device;
                    BeginReceive();
                }
                catch
                {
                    _device = null;
                    device?.Dispose();
                    throw;
                }
            }
        }

        protected override void Disconnect()
        {
            CancellationTokenSource? cancellation;
            Task? receiveTask;
            FtdiDevice? device;
            lock (this)
            {
                cancellation = _receiveCancellation;
                receiveTask = _receiveTask;
                device = _device;
                _receiveCancellation = null;
                _receiveTask = null;
                _device = null;
            }
            cancellation?.Cancel();
            device?.Dispose();
            DisposeCancellationWhenReceiveLoopStops(cancellation, receiveTask);
        }

        private void ConnectForProgramming()
        {
            lock (this)
            {
                if (_device != null) { throw new DeviceConnectionException("接続は既に開かれています。"); }
                if (_serialNumber == null) { throw new ArgumentNullException("serialNumber"); }
                FtdiDevice device = device = FtdiDevice.Open(_serialNumber);
                try
                {
                    device.ConfigureUart();
                    device.BaudRate = (uint)BaudRate;
                    EnterProgramMode(device);
                    _device = device;
                }
                catch
                {
                    _device = null;
                    device?.Dispose();
                    throw;
                }
            }
        }

        private void SwitchToSerialMonitor()
        {
            lock (this)
            {
                var device = RequireDevice();
                device.ConfigureUart();
                device.BaudRate = (uint)BaudRate;
                BeginReceive();
            }
        }

        internal override void ResetMCU()
        {
            lock (this)
            {
                var device = _device;
                if (device == null) { return; }
                const byte direction = 0x40;
                device.CbusBitMode = direction | 0x00;
                Thread.Sleep(500);
                device.CbusBitMode = direction | 0x04;
                Thread.Sleep(200);
                if (_receiveTask != null)
                {
                    device.ConfigureUart(false);
                    device.BaudRate = (uint)BaudRate;
                }
            }
        }

        internal void WriteExact(byte[] data)
        {
            lock (this)
            {
                RequireDevice().WriteExact(data);
            }
        }

        internal byte[] ReadExact(int size, double timeoutSeconds = 2.0)
        {
            lock (this)
            {
                return RequireDevice().ReadExact(size, timeoutSeconds);
            }
        }

        internal override void SendSerialMonitorText(string text)
        {
            if (text == null) { throw new ArgumentNullException("text"); }

            lock (this)
            {
                if (Mode != ConnectionMode.SerialMonitor)
                {
                    throw new InvalidOperationException(
                        "シリアルモニターモードではありません。");
                }

                RequireDevice().WriteExact(Encoding.UTF8.GetBytes(text));
            }
        }

        private void BeginReceive()
        {
            if (_receiveTask != null)
            {
                throw new InvalidOperationException("受信処理は既に開始されています。");
            }

            var decoder = Encoding.UTF8.GetDecoder();
            var cancellation = new CancellationTokenSource();
            _receiveCancellation = cancellation;
            try
            {
                _receiveTask = Task.Run(
                    () => ReceiveLoop(decoder, cancellation.Token));
            }
            catch
            {
                _receiveCancellation = null;
                cancellation.Dispose();
                throw;
            }
        }

        private void ReceiveLoop(Decoder decoder, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[] data;
                    lock (this)
                    {
                        if (_device == null) { return; }
                        data = _device.ReadAvailable();
                    }
                    if (data.Length == 0)
                    {
                        cancellationToken.WaitHandle.WaitOne(10);
                        continue;
                    }
                    var characters = new char[Encoding.UTF8.GetMaxCharCount(data.Length)];
                    var characterCount = decoder.GetChars(
                        data,
                        0,
                        data.Length,
                        characters,
                        0,
                        false);
                    if (characterCount > 0)
                    {
                        RaiseDataReceived(new string(characters, 0, characterCount));
                    }
                }
            }
            catch (DeviceConnectionException) { /* Ignore errors. */ }
            finally
            {
                Mode = ConnectionMode.Offline;
            }
        }

        private static void DisposeCancellationWhenReceiveLoopStops(
            CancellationTokenSource? cancellation,
            Task? receiveTask)
        {
            if (cancellation == null) { return; }
            if (receiveTask == null)
            {
                cancellation.Dispose();
                return;
            }

            receiveTask.ContinueWith(
                completedTask => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private FtdiDevice RequireDevice()
        {
            if (_device == null) { throw new DeviceConnectionException("接続が開かれていません。"); }
            return _device;
        }

        private static void EnterProgramMode(FtdiDevice device)
        {
            const byte direction = 0xC0;
            try
            {
                device.CbusBitMode = direction | 0x00;
                Thread.Sleep(100);
                device.CbusBitMode = direction | 0x04;
                Thread.Sleep(100);
                device.CbusBitMode = direction | 0x0C;
                Thread.Sleep(300);
                device.Purge();
            }
            catch (DeviceConnectionException)
            {
                try { device.CbusBitMode = direction | 0x0C; } catch { /* Ignore errors. */ }
                throw new DeviceConnectionException("書き込みモードへの移行に失敗しました。");
            }
        }
    }
}
