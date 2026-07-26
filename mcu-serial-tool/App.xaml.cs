using System;
using System.Windows;

namespace MCUSerialTool
{
    public partial class App : Application
    {
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        internal static uint ReadUInt32BE(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length != sizeof(uint))
            {
                throw new ArgumentException("4 バイト必要です。", nameof(data));
            }
            if (BitConverter.IsLittleEndian)
            {
                var bytes = (byte[])data.Clone();
                Array.Reverse(bytes);
                return BitConverter.ToUInt32(bytes, 0);

            }
            return BitConverter.ToUInt32(data, 0);
        }

        internal static uint ReadUInt32LE(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length != sizeof(uint))
            {
                throw new ArgumentException("4 バイト必要です。", nameof(data));
            }
            if (!BitConverter.IsLittleEndian)
            {
                var bytes = (byte[])data.Clone();
                Array.Reverse(bytes);
                return BitConverter.ToUInt32(bytes, 0);
            }
            return BitConverter.ToUInt32(data, 0);
        }

        internal static byte[] GetBytesUInt32BE(uint value)
        {
            var data = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(data);
            return data;
        }

        internal static byte[] GetBytesUInt32LE(uint value)
        {
            var data = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return data;
        }

        internal static byte[] GetBytesUInt16LE(ushort value)
        {
            var data = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(data);
            return data;
        }

        internal static byte CalculateXorChecksum(byte[] data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            byte checksum = 0;
            foreach (var value in data)
            {
                checksum ^= value;
            }
            return checksum;
        }

        internal static uint CalculateCrc32(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
            {
                crc = Crc32Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint value = 0; value < table.Length; value++)
            {
                var entry = value;
                for (var bit = 0; bit < 8; bit++)
                {
                    entry = (entry & 1) != 0
                        ? 0xEDB88320u ^ (entry >> 1)
                        : entry >> 1;
                }
                table[value] = entry;
            }
            return table;
        }
    }
}
