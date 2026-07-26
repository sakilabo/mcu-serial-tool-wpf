using FTD2XX_NET;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;

namespace MCUSerialTool
{
    internal partial class Connection
    {
        public static ConnectionDiscoveryResult Discovery()
        {
            var serialPorts = SerialPort.GetPortNames().ToArray();
            string? warning;
            var ftdiPorts = GetFtdiPorts(out warning);
            var connections = serialPorts
                .Concat(ftdiPorts.Select(ftdiPort => ftdiPort.Port))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => int.TryParse(Regex.Match(s, @"\d+$").Value, out var n) ? n : int.MaxValue)
                .ThenBy(port => port, StringComparer.OrdinalIgnoreCase)
                .Select(port => CreateConnection(
                    port,
                    ftdiPorts.FirstOrDefault(ftdiPort => string.Equals(
                        ftdiPort.Port,
                        port,
                        StringComparison.OrdinalIgnoreCase))))
                .ToList();

            return new ConnectionDiscoveryResult(connections, warning);
        }

        private static IReadOnlyList<FtdiDeviceInfo> GetFtdiPorts(
            out string? warning)
        {
            var result = new List<FtdiDeviceInfo>();
            warning = null;

            try
            {
                var ftdi = new FTDI();
                uint deviceCount = 0;
                var status = ftdi.GetNumberOfDevices(ref deviceCount);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    warning = "FTDIデバイスの列挙に失敗しました: " + status;
                    return result;
                }

                if (deviceCount == 0) { return result; }

                var devices = new FTDI.FT_DEVICE_INFO_NODE[deviceCount];
                status = ftdi.GetDeviceList(devices);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    warning = "FTDIデバイス情報の取得に失敗しました: " + status;
                    return result;
                }

                var unavailableCount = 0;
                for (uint index = 0; index < deviceCount; index++)
                {
                    var device = devices[index];
                    if (device == null)
                    {
                        unavailableCount++;
                        continue;
                    }

                    var opened = false;
                    try
                    {
                        status = ftdi.OpenByIndex(index);
                        if (status != FTDI.FT_STATUS.FT_OK)
                        {
                            unavailableCount++;
                            continue;
                        }

                        opened = true;
                        string comPort;
                        status = ftdi.GetCOMPort(out comPort);
                        if (status != FTDI.FT_STATUS.FT_OK
                            || string.IsNullOrWhiteSpace(comPort))
                        {
                            unavailableCount++;
                            continue;
                        }

                        result.Add(new FtdiDeviceInfo(
                            comPort,
                            device.Description,
                            device.SerialNumber,
                            (ushort)(device.ID >> 16),
                            (ushort)(device.ID & 0xFFFF)));
                    }
                    finally
                    {
                        if (opened)
                        {
                            ftdi.Close();
                        }
                    }
                }

                if (unavailableCount > 0)
                {
                    warning = unavailableCount
                        + "件のFTDIデバイスはCOMポート情報を取得できませんでした。";
                }
            }
            catch (Exception ex)
            {
                warning = "FTDIデバイス情報を取得できませんでした: " + ex.Message;
            }

            return result;
        }

        private static Connection CreateConnection(
            string port,
            FtdiDeviceInfo? ftdiDevice)
        {
            if (ftdiDevice == null) { return new Connection(port, "標準シリアルポート"); }

            var name = ftdiDevice.Description ?? "FTDI デバイス";

            // TWE-Programmer に定義されている名前 (TWE-Lite-R, TWE-Lite-USB)
            if (name.IndexOf("TWE-LITE-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TWEConnection(
                    port,
                    name,
                    ftdiDevice.SerialNumber,
                    [typeof(TWE516xMCU), typeof(TWE518xMCU)]);
            }

            // TWE-Programmer に定義されている名前
            if (name.IndexOf("MONOSTICK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TWEConnection(
                    port,
                    name,
                    ftdiDevice.SerialNumber,
                    [typeof(TWE516xMCU)]);
            }

            // 実機で確認している名前
            if (name.IndexOf("TWELITE-STICK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TWEConnection(
                    port,
                    name,
                    ftdiDevice.SerialNumber,
                    [typeof(TWE518xMCU)]);
            }

            return new Connection(port, name);
        }

        private sealed class FtdiDeviceInfo
        {
            public FtdiDeviceInfo(
                string port,
                string? description,
                string? serialNumber,
                ushort vendorId,
                ushort productId)
            {
                Port = port;
                Description = description;
                SerialNumber = serialNumber;
                VendorId = vendorId;
                ProductId = productId;
            }

            public string Port { get; private set; }

            public string? Description { get; private set; }

            public string? SerialNumber { get; private set; }

            public ushort VendorId { get; private set; }

            public ushort ProductId { get; private set; }
        }
    }

    internal sealed class ConnectionDiscoveryResult
    {
        public ConnectionDiscoveryResult(
            IReadOnlyList<Connection> connections,
            string? warning)
        {
            Connections = connections;
            Warning = warning;
        }

        public IReadOnlyList<Connection> Connections { get; private set; }

        public string? Warning { get; private set; }
    }
}
