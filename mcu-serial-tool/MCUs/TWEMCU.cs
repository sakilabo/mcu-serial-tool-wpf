namespace MCUSerialTool
{
    internal abstract class TWEMCU : MCU
    {
        protected TWEMCU(TWEConnection connection) : base(connection)
        {
            TWEConnection = connection;
        }

        protected TWEConnection TWEConnection { get; }

        protected abstract uint ISPBaudRate { get; }

        protected sealed override void WriteFirmware(Firmware firmware, bool verify)
        {
            var baudRate = TWEConnection.BaudRate;
            try
            {
                TWEConnection.BaudRate = (int)ISPBaudRate;
                TWEConnection.Mode = ConnectionMode.Programming;
                try
                {
                    WriteFirmwareToMcu(firmware, verify);
                    TWEConnection.BaudRate = baudRate;
                    TWEConnection.Mode = ConnectionMode.SerialMonitor;
                }
                finally
                {
                    TWEConnection.ResetMCU();
                }
            }
            finally
            {
                if (TWEConnection.Mode != ConnectionMode.SerialMonitor)
                {
                    TWEConnection.Mode = ConnectionMode.Offline;
                }
                TWEConnection.BaudRate = baudRate;
            }
        }

        protected abstract void WriteFirmwareToMcu(Firmware firmware, bool verify);

        protected static uint SerialIdFromMac(byte[] mac)
        {
            if (mac.Length != 8) { throw new DeviceConnectionException("MAC アドレスの長さが不正です。"); }

            var suffix = ((uint)mac[4] << 24)
                | ((uint)mac[5] << 16)
                | ((uint)mac[6] << 8)
                | mac[7];
            return 0x80000000u | (suffix & 0x0FFFFFFF);
        }
    }
}
