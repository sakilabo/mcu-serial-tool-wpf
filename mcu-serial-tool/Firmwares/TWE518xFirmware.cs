using System.Linq;

namespace MCUSerialTool
{
    internal static class TWE518xFirmware
    {
        private const int VectorTableLength = 0x20;
        private const int ImageMarkerOffset = 0x20;
        private const int BootBlockOffsetOffset = 0x24;
        private const int HeaderCrcOffset = 0x28;
        private const int HeaderLength = 0x2C;
        private const int BootBlockImageLengthOffset = 0x0C;
        private const int BootBlockMinimumLength = 0x10;
        private const uint SramAddressMask = 0xFF000000;
        private const uint SramAddressPrefix = 0x04000000;
        private const uint MaximumFlashAddress = 0x000A0000;
        private const uint ImageMarker = 0x98447902;
        private const uint BootBlockMarker = 0xBB0110BB;

        internal static bool TryLoad(
            byte[] fileData,
            out FirmwareType type,
            out byte[]? data)
        {
            type = FirmwareType.UNKNOWN;
            data = null;
            if (fileData.Length < HeaderLength) { return false; }

            var imageMarker = fileData
                .Skip(ImageMarkerOffset)
                .Take(sizeof(uint))
                .ToArray();
            if (App.ReadUInt32LE(imageMarker) != ImageMarker) { return false; }

            Validate(fileData);
            type = FirmwareType.TWELITE_GOLD;
            data = fileData;
            return true;
        }

        private static void Validate(byte[] fileData)
        {
            var initialStackPointer = App.ReadUInt32LE(
                fileData.Take(sizeof(uint)).ToArray());
            if ((initialStackPointer & SramAddressMask) != SramAddressPrefix
                || (initialStackPointer & 0x03) != 0)
            {
                throw new FirmwareValidationException(
                    "JN518x 初期スタックポインタ");
            }

            var resetVector = App.ReadUInt32LE(
                fileData.Skip(0x04).Take(sizeof(uint)).ToArray());
            var resetAddress = resetVector & ~1u;
            if ((resetVector & 1) == 0
                || resetAddress >= MaximumFlashAddress
                || resetAddress >= (uint)fileData.Length)
            {
                throw new FirmwareValidationException(
                    "JN518x リセットベクタ");
            }

            uint vectorSum = 0;
            for (var offset = 0; offset < VectorTableLength; offset += 4)
            {
                var vector = fileData
                    .Skip(offset)
                    .Take(sizeof(uint))
                    .ToArray();
                vectorSum = unchecked(vectorSum + App.ReadUInt32LE(vector));
            }

            if (vectorSum != 0)
            {
                throw new FirmwareValidationException(
                    "JN518x ベクタテーブルチェックサム");
            }

            var headerCrc = fileData
                .Skip(HeaderCrcOffset)
                .Take(sizeof(uint))
                .ToArray();
            var expectedHeaderCrc = App.ReadUInt32LE(headerCrc);
            var header = fileData.Take(HeaderCrcOffset).ToArray();
            var actualHeaderCrc = App.CalculateCrc32(header);
            if (actualHeaderCrc != expectedHeaderCrc)
            {
                throw new FirmwareValidationException(
                    "JN518x ヘッダ CRC");
            }

            var bootBlockOffsetBytes = fileData
                .Skip(BootBlockOffsetOffset)
                .Take(sizeof(uint))
                .ToArray();
            var bootBlockOffset = App.ReadUInt32LE(bootBlockOffsetBytes);
            if ((bootBlockOffset & 0x03) != 0
                || bootBlockOffset
                    > (uint)fileData.Length - BootBlockMinimumLength)
            {
                throw new FirmwareValidationException(
                    "JN518x ブートブロック位置");
            }

            var bootBlockIndex = (int)bootBlockOffset;
            var bootBlockMarker = fileData
                .Skip(bootBlockIndex)
                .Take(sizeof(uint))
                .ToArray();
            if (App.ReadUInt32LE(bootBlockMarker) != BootBlockMarker)
            {
                throw new FirmwareValidationException(
                    "JN518x ブートブロック");
            }

            var imageLengthBytes = fileData
                .Skip(bootBlockIndex + BootBlockImageLengthOffset)
                .Take(sizeof(uint))
                .ToArray();
            var imageLength = App.ReadUInt32LE(imageLengthBytes);
            if (imageLength != (uint)fileData.Length)
            {
                throw new FirmwareValidationException(
                    "JN518x イメージ長");
            }
        }
    }
}
