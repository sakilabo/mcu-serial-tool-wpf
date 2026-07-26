using System;
using System.IO;
using System.Security;

namespace MCUSerialTool
{
    internal enum FirmwareType
    {
        UNKNOWN = 0,
        TWELITE_BLUE,
        TWELITE_RED,
        TWELITE_GOLD
    }

    internal sealed class Firmware
    {
        private Firmware(string filePath)
        {
            FilePath = filePath;
            Data = Array.Empty<byte>();
            Reload();
        }

        public string FilePath { get; private set; }

        public FirmwareType Type { get; private set; }

        internal byte[] Data { get; private set; }

        public static Firmware Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException(
                    "ファイルが指定されていません。",
                    "filePath");
            }

            return new Firmware(filePath);
        }

        internal void Reload()
        {
            try
            {
                var fileData = ReadFile();
                FirmwareType type;
                byte[]? data;
                var loaded = TWE516xFirmware.TryLoad(
                    fileData,
                    out type,
                    out data)
                    || TWE518xFirmware.TryLoad(
                        fileData,
                        out type,
                        out data);
                if (!loaded || data == null)
                {
                    Type = FirmwareType.UNKNOWN;
                    Data = Array.Empty<byte>();
                    return;
                }

                Type = type;
                Data = data;
            }
            catch (FirmwareException)
            {
                Type = FirmwareType.UNKNOWN;
                Data = Array.Empty<byte>();
            }
        }

        private byte[] ReadFile()
        {
            try
            {
                return File.ReadAllBytes(FilePath);
            }
            catch (Exception ex) when (
                ex is ArgumentException
                || ex is IOException
                || ex is UnauthorizedAccessException
                || ex is NotSupportedException
                || ex is SecurityException)
            {
                throw new FirmwareReadException(ex.Message, ex);
            }
        }
    }
}
