using System;
using System.Linq;

namespace MCUSerialTool
{
    internal static class TWE516xFirmware
    {
        private static readonly byte[] BlueMarker =
        {
            0x04, 0x03, 0x00, 0x08
        };
        private static readonly byte[] RedMarker =
        {
            0x0F, 0x03, 0x00, 0x0B
        };
        private static readonly byte[] BootImageRecordMarker =
        {
            0x12, 0x34, 0x56, 0x78,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88
        };

        internal static bool TryLoad(
            byte[] fileData,
            out FirmwareType type,
            out byte[]? data)
        {
            byte[] marker;
            if (fileData.Take(BlueMarker.Length).SequenceEqual(BlueMarker))
            {
                type = FirmwareType.TWELITE_BLUE;
                marker = BlueMarker;
            }
            else if (fileData.Take(RedMarker.Length).SequenceEqual(RedMarker))
            {
                type = FirmwareType.TWELITE_RED;
                marker = RedMarker;
            }
            else
            {
                type = FirmwareType.UNKNOWN;
                data = null;
                return false;
            }

            var bootImageRecord = fileData
                .Skip(marker.Length)
                .Take(BootImageRecordMarker.Length);
            if (!bootImageRecord.SequenceEqual(BootImageRecordMarker))
            {
                throw new FirmwareValidationException(
                    "JN516x Boot Image Record");
            }

            data = new byte[fileData.Length - marker.Length];
            Array.Copy(fileData, marker.Length, data, 0, data.Length);
            return true;
        }
    }
}
