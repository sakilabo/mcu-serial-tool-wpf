using FTD2XX_NET;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace MCUSerialTool
{
    internal sealed class FtdiDevice : IDisposable
    {
        private const byte CbusMode = 0x20;

        private readonly FTDI _device;
        private bool _open;

        private FtdiDevice(FTDI device)
        {
            _device = device;
        }

        public static FtdiDevice Open(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber))
            {
                throw new DeviceConnectionException("FTDI のシリアル番号が不明です。");
            }

            var device = new FTDI();
            FTDI.FT_STATUS status;
            try
            {
                status = device.OpenBySerialNumber(serialNumber);
            }
            catch (Exception ex)
            {
                throw new DeviceConnectionException("選択した FTDI を開けません: " + ex.Message);
            }

            if (status != FTDI.FT_STATUS.FT_OK)
            {
                var msg = "選択した FTDI を開けません: " + status;
                throw new DeviceConnectionException(msg);
            }

            var result = new FtdiDevice(device);
            result._open = true;
            return result;
        }

        public uint BaudRate
        {
            set
            {
                Check(_device.SetBaudRate(value), "ボーレート設定");
            }
        }

        public void ConfigureUart(bool purge = true)
        {
            Check(_device.SetBitMode(0x00, 0x00), "UART モード設定");
            Check(
                _device.SetDataCharacteristics(
                    FTDI.FT_DATA_BITS.FT_BITS_8,
                    FTDI.FT_STOP_BITS.FT_STOP_BITS_1,
                    FTDI.FT_PARITY.FT_PARITY_NONE),
                "通信形式設定");
            Check(
                _device.SetFlowControl(FTDI.FT_FLOW_CONTROL.FT_FLOW_NONE, 0, 0),
                "フロー制御設定");
            Check(_device.SetTimeouts(100, 500), "タイムアウト設定");
            Check(_device.SetLatency(2), "レイテンシ設定");
            if (purge) { Purge(); }
        }

        public byte CbusBitMode
        {
            set
            {
                Check(_device.SetBitMode(value, CbusMode), "CBUS ビットモード設定");
            }
        }

        public void Purge()
        {
            Check(
                _device.Purge(
                    (uint)(FTDI.FT_PURGE.FT_PURGE_RX
                        | FTDI.FT_PURGE.FT_PURGE_TX)),
                "バッファクリア");
        }

        public void WriteExact(byte[] data)
        {
            uint written = 0;
            var status = _device.Write(data, data.Length, ref written);
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                var msg = "FTDI への送信に失敗しました: " + status;
                throw new DeviceConnectionException(msg);
            }

            if (written != data.Length)
            {
                var msg = "FTDI への送信が完了しませんでした: "
                    + written + "/" + data.Length + " bytes";
                throw new DeviceConnectionException(msg);
            }
        }

        public byte[] ReadExact(int size, double timeoutSeconds = 2.0)
        {
            var received = new List<byte>(size);
            var buffer = new byte[size];
            var stopwatch = Stopwatch.StartNew();
            while (received.Count < size
                && stopwatch.Elapsed.TotalSeconds < timeoutSeconds)
            {
                var toRead = (uint)(size - received.Count);
                uint read = 0;
                var status = _device.Read(buffer, toRead, ref read);
                if (status != FTDI.FT_STATUS.FT_OK)
                {
                    var msg = "FTDI の読み出しに失敗しました: " + status;
                    throw new DeviceConnectionException(msg);
                }

                for (uint index = 0; index < read; index++)
                {
                    received.Add(buffer[index]);
                }
            }

            return received.ToArray();
        }

        public byte[] ReadAvailable()
        {
            uint available = 0;
            Check(_device.GetRxBytesAvailable(ref available), "受信データ量取得");
            if (available == 0) { return new byte[0]; }

            var buffer = new byte[available];
            uint read = 0;
            Check(_device.Read(buffer, available, ref read), "FTDI の読み出し");
            if (read == available) { return buffer; }

            var result = new byte[read];
            Array.Copy(buffer, result, read);
            return result;
        }

        public void Dispose()
        {
            if (!_open) { return; }

            _open = false;
            try { _device.Close(); } catch { /* Ignore errors. */ }
        }

        private static void Check(FTDI.FT_STATUS status, string operation)
        {
            if (status != FTDI.FT_STATUS.FT_OK)
            {
                var msg = operation + "に失敗しました: " + status;
                throw new DeviceConnectionException(msg);
            }
        }
    }
}
