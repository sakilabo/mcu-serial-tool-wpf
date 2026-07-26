using System;
using System.Linq;

namespace MCUSerialTool
{
    internal sealed class TWE516xMCU : TWEMCU
    {
        private const byte GetChipIdCommand = 0x32;
        private const byte SelectFlashTypeCommand = 0x2C;
        private const byte EraseFlashCommand = 0x07;
        private const byte ChangeBaudRateCommand = 0x27;
        private const byte WriteFlashCommand = 0x09;
        private const byte ReadFlashCommand = 0x0B;
        private const byte ReadRamCommand = 0x1F;
        private const int WriteChunkSize = 0x80;
        private const uint ChipPartBlue = 0x00008686;
        private const uint ChipPartRed = 0x0000B686;
        private const uint ChipPartMask = 0x003FFFFF;
        private const uint OverriddenMacAddress = 0x01001570;
        private const uint FactoryMacAddress = 0x01001580;
        public TWE516xMCU(TWEConnection connection) : base(connection) { }

        protected override uint ISPBaudRate
        {
            get { return 38400; }
        }

        protected override bool SupportsFirmware(
            FirmwareType firmwareType)
        {
            return firmwareType == FirmwareType.TWELITE_BLUE
                || firmwareType == FirmwareType.TWELITE_RED;
        }

        protected override void WriteFirmwareToMcu(Firmware firmware, bool verify)
        {
            var chipId = GetChipId();
            var model = (chipId & ChipPartMask) == ChipPartBlue ? "BLUE" : "RED";
            var mac = GetEffectiveMac();
            var sid = SerialIdFromMac(mac);

            ReportMessage("TWELITE " + model + " (SID: 0x" + sid.ToString("X8") + ") に接続しました。");

            var expectedModel = model == "BLUE"
                ? FirmwareType.TWELITE_BLUE
                : FirmwareType.TWELITE_RED;
            if (firmware.Type != expectedModel)
            {
                throw new FirmwareCompatibilityException(
                    "選択したファームウェアは TWELITE "
                    + model + " 用ではありません。");
            }

            var writeData = firmware.Data;

            var totalWork = (long)writeData.Length * (verify ? 2 : 1);
            long completed = 0;
            ReportProgress(completed, totalWork);
            SelectInternalFlash();
            ReportMessage("Flash を消去します。");
            EraseFlash();
            ChangeMcuBaudRate(1000000);
            TWEConnection.BaudRate = 1000000;
            ReportMessage("アプリの書き込みを開始します。");
            WriteFlash(writeData, ref completed, totalWork);
            if (verify)
            {
                ReportMessage("ベリファイを開始します。");
                VerifyFirmware(writeData, ref completed, totalWork);
            }
            ChangeMcuBaudRate(38400);
            TWEConnection.BaudRate = 38400;
            ReportMessage("アプリの書き込みが完了しました。");
        }

        private void WriteFlash(byte[] firmware, ref long completed, long totalWork)
        {
            var total = firmware.Length;
            var remainder = total % WriteChunkSize;
            var startAddress = total - (remainder != 0 ? remainder : WriteChunkSize);
            var chunkLength = total - startAddress;

            for (var address = startAddress; address >= 0; address -= WriteChunkSize)
            {
                var chunk = new byte[chunkLength];
                Array.Copy(firmware, address, chunk, 0, chunkLength);
                WriteFlashChunk((uint)address, chunk);
                completed += chunk.Length;
                ReportProgress(completed, totalWork);
                chunkLength = WriteChunkSize;
            }
        }

        private void VerifyFirmware(byte[] firmware, ref long completed, long totalWork)
        {
            for (var address = 0; address < firmware.Length; address += WriteChunkSize)
            {
                var chunkLength = Math.Min(WriteChunkSize, firmware.Length - address);
                var read = ReadFlash((uint)address, (ushort)chunkLength);
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

        private void SelectInternalFlash()
        {
            var payload = new byte[5];
            payload[0] = 8;
            App.GetBytesUInt32LE(0).CopyTo(payload, 1);
            RequireSuccess(
                SendRequest(SelectFlashTypeCommand, payload),
                "内蔵 Flash 選択");
        }

        private void EraseFlash()
        {
            RequireSuccess(
                SendRequest(EraseFlashCommand, []),
                "Flash 消去");
        }

        private void ChangeMcuBaudRate(uint baudRate)
        {
            uint divisor;
            switch (baudRate)
            {
                case 1000000:
                    divisor = 1;
                    break;
                case 500000:
                    divisor = 2;
                    break;
                case 115200:
                    divisor = 9;
                    break;
                case 38400:
                    divisor = 26;
                    break;
                default:
                    throw new DeviceConnectionException(
                        "未対応のボーレートです: " + baudRate);
            }

            var payload = new byte[4];
            App.GetBytesUInt32LE(divisor).CopyTo(payload, 0);
            RequireSuccess(
                SendRequest(ChangeBaudRateCommand, payload),
                "MCU ボーレート変更");
        }

        private void WriteFlashChunk(uint address, byte[] data)
        {
            var payload = new byte[4 + data.Length];
            App.GetBytesUInt32LE(address).CopyTo(payload, 0);
            Array.Copy(data, 0, payload, 4, data.Length);
            RequireSuccess(
                SendRequest(WriteFlashCommand, payload),
                "Flash 書き込み 0x" + address.ToString("X8"));
        }

        private byte[] ReadFlash(uint address, ushort length)
        {
            var payload = new byte[6];
            App.GetBytesUInt32LE(address).CopyTo(payload, 0);
            App.GetBytesUInt16LE(length).CopyTo(payload, 4);

            var response = SendRequest(ReadFlashCommand, payload);
            RequireSuccess(response, "Flash 読み出し 0x" + address.ToString("X8"));
            var data = new byte[response.Length - 1];
            Array.Copy(response, 1, data, 0, data.Length);
            if (data.Length != length)
            {
                var msg = "Flash 読み出し長が不正です: "
                    + data.Length + "/" + length + " bytes";
                throw new DeviceConnectionException(msg);
            }

            return data;
        }

        private uint GetChipId()
        {
            var response = SendRequest(GetChipIdCommand, new byte[0]);
            if (response.Length != 5 && response.Length != 9)
            {
                var msg = "JN516x の Chip ID 応答長が不正です: " + response.Length;
                throw new DeviceConnectionException(msg);
            }
            RequireSuccess(response, "Chip ID 取得");
            var chipId = App.ReadUInt32BE(
                response.Skip(1).Take(sizeof(uint)).ToArray());
            var supportedPart = chipId & ChipPartMask;
            var isSupportedPart = supportedPart == ChipPartBlue
                || supportedPart == ChipPartRed;
            if (!isSupportedPart)
            {
                var msg = "未対応の Chip ID です: 0x" + chipId.ToString("X8");
                throw new DeviceConnectionException(msg);
            }
            return chipId;
        }

        private byte[] GetEffectiveMac()
        {
            var mac = ReadRam(OverriddenMacAddress, 8);
            if (mac.All(element => element == 0xFF))
            {
                mac = ReadRam(FactoryMacAddress, 8);
            }
            return mac;
        }

        private byte[] ReadRam(uint address, ushort length)
        {
            var payload = new byte[6];
            App.GetBytesUInt32LE(address).CopyTo(payload, 0);
            App.GetBytesUInt16LE(length).CopyTo(payload, 4);
            var response = SendRequest(ReadRamCommand, payload);
            RequireSuccess(response, "RAM 読み出し 0x" + address.ToString("X8"));
            var data = new byte[response.Length - 1];
            Array.Copy(response, 1, data, 0, data.Length);
            if (data.Length != length)
            {
                var msg = "RAM 読み出し長が不正です: " + data.Length + "/" + length + " bytes";
                throw new DeviceConnectionException(msg);
            }
            return data;
        }

        private static void RequireSuccess(byte[] response, string operation)
        {
            if (response.Length == 0)
            {
                var msg = operation + " の応答にステータスがありません。";
                throw new DeviceConnectionException(msg);
            }
            if (response[0] != 0)
            {
                var msg = operation + " に失敗しました: status=0x" + response[0].ToString("X2");
                throw new DeviceConnectionException(msg);
            }
        }

        private byte[] SendRequest(byte requestType, byte[] payload)
        {
            var messageLength = payload.Length + 2;
            if (messageLength > 0xFF)
            {
                throw new DeviceConnectionException("JN516x への送信データが長すぎます。");
            }

            var packet = new byte[messageLength + 1];
            packet[0] = (byte)messageLength;
            packet[1] = requestType;
            Array.Copy(payload, 0, packet, 2, payload.Length);
            var checksumData = packet.Take(packet.Length - 1).ToArray();
            packet[packet.Length - 1] = App.CalculateXorChecksum(checksumData);
            TWEConnection.WriteExact(packet);

            var header = TWEConnection.ReadExact(2);
            if (header.Length != 2)
            {
                var msg = "JN516x から応答がありません: command=0x"
                    + requestType.ToString("X2");
                throw new DeviceConnectionException(msg);
            }

            int responseLength = header[0];
            int responseType = header[1];
            if (responseLength < 2)
            {
                var msg = "JN516x から不正な応答長を受信しました: " + responseLength;
                throw new DeviceConnectionException(msg);
            }

            if (responseType != requestType + 1)
            {
                var msg = "JN516x から不正な応答種別を受信しました: 0x"
                    + responseType.ToString("X2");
                throw new DeviceConnectionException(msg);
            }

            var body = TWEConnection.ReadExact(responseLength - 1);
            if (body.Length != responseLength - 1)
            {
                throw new DeviceConnectionException("JN516x の応答が途中で途切れました。");
            }

            if (App.CalculateXorChecksum(header) != App.CalculateXorChecksum(body))
            {
                throw new DeviceConnectionException("JN516x の応答チェックサムが一致しません。");
            }

            var result = new byte[body.Length - 1];
            Array.Copy(body, 0, result, 0, result.Length);
            return result;
        }
    }
}
