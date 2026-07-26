using System;
using System.Linq;

namespace MCUSerialTool
{
    internal sealed class TWE518xMCU : TWEMCU
    {
        private const byte GetDeviceInfoCommand = 0x32;
        private const byte SetBaudRateCommand = 0x27;
        private const byte OpenMemoryCommand = 0x40;
        private const byte EraseMemoryCommand = 0x42;
        private const byte BlankCheckMemoryCommand = 0x44;
        private const byte ReadMemoryCommand = 0x46;
        private const byte WriteMemoryCommand = 0x48;
        private const byte CloseMemoryCommand = 0x4A;
        private const byte UnlockIspCommand = 0x4E;
        private const byte DefaultIspMode = 0x00;
        private const byte StartIspMode = 0x01;
        private const byte FlashMemoryId = 0;
        private const byte PflashMemoryId = 2;
        private const byte ConfigMemoryId = 3;
        private const byte ReadAccessMode = 0x01;
        private const byte FlashAccessMode = 0x01 | 0x02 | 0x04;
        private const int WriteChunkSize = 512;
        private static readonly byte[] IspKey =
        {
            0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88,
            0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88
        };

        public TWE518xMCU(TWEConnection connection) : base(connection) { }

        protected override uint ISPBaudRate
        {
            get { return 115200; }
        }

        protected override bool SupportsFirmware(
            FirmwareType firmwareType)
        {
            return firmwareType == FirmwareType.TWELITE_GOLD;
        }

        protected override void WriteFirmwareToMcu(Firmware firmware, bool verify)
        {
            EnterDefaultIspMode();
            ValidateDeviceInfoResponse();
            StartIsp();
            var mac = GetEffectiveMac();
            var sid = SerialIdFromMac(mac);

            ReportMessage("TWELITE GOLD (SID: 0x" + sid.ToString("X8") + ") に接続しました。");
            var writeData = firmware.Data;

            var totalWork = (long)writeData.Length * (verify ? 2 : 1);
            long completed = 0;
            ReportProgress(completed, totalWork);
            ChangeMcuBaudRate(1000000);
            TWEConnection.BaudRate = 1000000;
            using (var handle = OpenMemory(FlashMemoryId, FlashAccessMode))
            {
                ReportMessage("Flash を消去します。");
                EraseFlash(handle, (uint)writeData.Length);
                ReportMessage("アプリの書き込みを開始します。");
                WriteFlash(handle, writeData, ref completed, totalWork);
                if (verify)
                {
                    ReportMessage("ベリファイしています。");
                    VerifyFirmware(handle, writeData, ref completed, totalWork);
                }
            }
            ChangeMcuBaudRate(115200);
            TWEConnection.BaudRate = 115200;
            ReportMessage("書き込みが完了しました。");
        }

        private void WriteFlash(
            MemoryHandle handle,
            byte[] firmware,
            ref long completed,
            long totalWork)
        {
            var total = firmware.Length;
            for (var address = 0; address < total; address += WriteChunkSize)
            {
                var chunkLength = Math.Min(WriteChunkSize, total - address);
                var chunk = new byte[chunkLength];
                Array.Copy(firmware, address, chunk, 0, chunkLength);
                handle.WriteMemory((uint)address, chunk);
                completed += chunk.Length;
                ReportProgress(completed, totalWork);
            }
        }

        private void VerifyFirmware(
            MemoryHandle handle,
            byte[] firmware,
            ref long completed,
            long totalWork)
        {
            for (var address = 0; address < firmware.Length; address += WriteChunkSize)
            {
                var chunkLength = Math.Min(WriteChunkSize, firmware.Length - address);
                var read = handle.ReadMemory((uint)address, (uint)chunkLength);
                for (var index = 0; index < chunkLength; index++)
                {
                    if (read[index] != firmware[address + index])
                    {
                        var msg = "ベリファイに失敗しました: address=0x"
                            + (address + index).ToString("X8");
                        throw new FirmwareVerificationException(msg);
                    }
                }
                completed += chunkLength;
                ReportProgress(completed, totalWork);
            }
        }

        private void EraseFlash(MemoryHandle handle, uint length)
        {
            handle.EraseMemory(0, length);
            handle.BlankCheckMemory(0, length);
        }

        private void EnterDefaultIspMode()
        {
            SendRequest(UnlockIspCommand, [DefaultIspMode]);
        }

        private void ValidateDeviceInfoResponse()
        {
            var info = SendRequest(GetDeviceInfoCommand, []);
            if (info.Length != 8)
            {
                var msg = "JN518x の Device Info 応答長が不正です: " + info.Length;
                throw new DeviceConnectionException(msg);
            }
        }

        private void StartIsp()
        {
            var payload = new byte[1 + IspKey.Length];
            payload[0] = StartIspMode;
            Array.Copy(IspKey, 0, payload, 1, IspKey.Length);
            SendRequest(UnlockIspCommand, payload);
        }

        private void ChangeMcuBaudRate(uint baudRate)
        {
            var payload = new byte[5];
            payload[0] = 0x00;
            App.GetBytesUInt32LE(baudRate).CopyTo(payload, 1);
            SendRequest(
                SetBaudRateCommand,
                payload,
                "MCU ボーレート変更");
        }

        private byte[] GetEffectiveMac()
        {
            byte[] mac;
            using (var handle = OpenMemory(PflashMemoryId, ReadAccessMode))
            {
                mac = handle.ReadMemory(0x00000040, 8);
            }

            var isBlank = mac.All(element => element == 0x00)
                || mac.All(element => element == 0xFF);
            if (isBlank)
            {
                using (var handle = OpenMemory(ConfigMemoryId, ReadAccessMode))
                {
                    mac = handle.ReadMemory(0x0009FC70, 8);
                }
            }

            Array.Reverse(mac);
            return mac;
        }

        private MemoryHandle OpenMemory(byte memoryId, byte accessMode)
        {
            var response = SendRequest(OpenMemoryCommand, [memoryId, accessMode]);
            if (response.Length != 1)
            {
                var msg = "メモリオープン応答長が不正です: " + response.Length;
                throw new DeviceConnectionException(msg);
            }
            return new MemoryHandle(this, response[0]);
        }

        private sealed class MemoryHandle : IDisposable
        {
            private readonly TWE518xMCU _mcu;
            private readonly byte _handle;

            internal MemoryHandle(TWE518xMCU mcu, byte handle)
            {
                _mcu = mcu;
                _handle = handle;
            }

            internal void EraseMemory(uint address, uint length)
            {
                var payload = BuildRangePayload(address, length);
                _mcu.SendRequest(EraseMemoryCommand, payload);
            }

            internal void BlankCheckMemory(uint address, uint length)
            {
                var payload = BuildRangePayload(address, length);
                _mcu.SendRequest(BlankCheckMemoryCommand, payload);
            }

            internal byte[] ReadMemory(uint address, uint length)
            {
                var payload = BuildRangePayload(address, length);
                var data = _mcu.SendRequest(ReadMemoryCommand, payload);
                if (data.Length != length)
                {
                    var msg = "メモリ読み出し長が不正です: "
                        + data.Length + "/" + length + " bytes";
                    throw new DeviceConnectionException(msg);
                }
                return data;
            }

            internal void WriteMemory(uint address, byte[] data)
            {
                var payload = new byte[10 + data.Length];
                BuildRangePayload(address, (uint)data.Length).CopyTo(payload, 0);
                data.CopyTo(payload, 10);
                _mcu.SendRequest(WriteMemoryCommand, payload);
            }

            public void Dispose()
            {
                _mcu.SendRequest(CloseMemoryCommand, [_handle]);
            }

            private byte[] BuildRangePayload(uint address, uint length)
            {
                var payload = new byte[10];
                payload[0] = _handle;
                payload[1] = 0x00;
                App.GetBytesUInt32LE(address).CopyTo(payload, 2);
                App.GetBytesUInt32LE(length).CopyTo(payload, 6);
                return payload;
            }

        }

        private byte[] SendRequest(
            byte command,
            byte[] payload,
            string? operation = null)
        {
            TWEConnection.WriteExact(BuildPacket(command, payload));

            var header = TWEConnection.ReadExact(4);
            if (header.Length != 4)
            {
                var msg = "JN518x から応答がありません: command=0x" + command.ToString("X2");
                throw new DeviceConnectionException(msg);
            }

            int responseLength = (header[1] << 8) | header[2];
            int responseType = header[3];
            if (responseLength < 9)
            {
                var msg = "JN518x から不正な応答長を受信しました: " + responseLength;
                throw new DeviceConnectionException(msg);
            }

            if (responseType != command + 1)
            {
                var msg = "JN518x から不正な応答種別を受信しました: 0x" + responseType.ToString("X2");
                throw new DeviceConnectionException(msg);
            }

            var body = TWEConnection.ReadExact(responseLength - 4);
            if (body.Length != responseLength - 4)
            {
                throw new DeviceConnectionException("JN518x の応答が途中で途切れました。");
            }

            var packet = new byte[header.Length + body.Length];
            Array.Copy(header, 0, packet, 0, header.Length);
            Array.Copy(body, 0, packet, header.Length, body.Length);

            var crc = packet.Skip(packet.Length - sizeof(uint)).Take(sizeof(uint)).ToArray();
            var expectedCrc = App.ReadUInt32BE(crc);
            var crcData = packet.Take(packet.Length - sizeof(uint)).ToArray();
            var actualCrc = App.CalculateCrc32(crcData);
            if (actualCrc != expectedCrc)
            {
                throw new DeviceConnectionException("JN518x の応答 CRC が一致しません。");
            }

            var status = body[0];
            if (status != 0)
            {
                if (!string.IsNullOrEmpty(operation))
                {
                    var msg = operation + " に失敗しました: status=0x"
                        + status.ToString("X2");
                    throw new DeviceConnectionException(msg);
                }
                var errorMessage = "JN518x がエラーを返しました: command=0x"
                    + command.ToString("X2")
                    + ", status=0x" + status.ToString("X2");
                throw new DeviceConnectionException(errorMessage);
            }
            var result = new byte[body.Length - 5];
            Array.Copy(body, 1, result, 0, result.Length);
            return result;
        }

        private static byte[] BuildPacket(byte command, byte[] payload)
        {
            var packetLength = 1 + 2 + 1 + payload.Length + sizeof(uint);
            var packet = new byte[packetLength];
            packet[0] = 0x00;
            packet[1] = (byte)((packetLength >> 8) & 0xFF);
            packet[2] = (byte)(packetLength & 0xFF);
            packet[3] = command;
            Array.Copy(payload, 0, packet, 4, payload.Length);
            var crcData = packet.Take(packetLength - sizeof(uint)).ToArray();
            var crc = App.CalculateCrc32(crcData);
            App.GetBytesUInt32BE(crc).CopyTo(packet, packetLength - sizeof(uint));
            return packet;
        }
    }
}
