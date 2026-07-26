using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MCUSerialTool
{
    internal sealed class SerialPortSettingsStore
    {
        private const string Header = "Port,Name,BaudRate";
        private static readonly Encoding FileEncoding = new UTF8Encoding(false);

        public SerialPortSettingsStore()
        {
            FilePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "serial-port-settings.csv");
        }

        public string FilePath { get; private set; }

        public string? Apply(
            IEnumerable<Connection> connections)
        {
            foreach (var connection in connections)
            {
                connection.BaudRate = Connection.DefaultBaudRate;
            }

            if (!File.Exists(FilePath)) { return null; }

            try
            {
                var settings = ReadSettings();
                foreach (var connection in connections)
                {
                    var setting = settings.FirstOrDefault(item => item.Matches(connection));
                    if (setting != null && connection.SupportsBaudRate(setting.BaudRate))
                    {
                        connection.BaudRate = setting.BaudRate;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return "通信速度設定を読み込めませんでした: " + ex.Message;
            }
        }

        public string? Save(Connection connection)
        {
            try
            {
                var settings = File.Exists(FilePath)
                    ? ReadSettings()
                    : new List<SerialPortSetting>();
                var setting = settings.FirstOrDefault(item => item.Matches(connection));
                if (setting == null)
                {
                    setting = SerialPortSetting.FromConnection(connection);
                    settings.Add(setting);
                }
                else
                {
                    setting.BaudRate = connection.BaudRate;
                }

                var lines = new List<string> { Header };
                lines.AddRange(settings.Select(item => item.ToCsvLine()));
                File.WriteAllLines(FilePath, lines, FileEncoding);
                return null;
            }
            catch (Exception ex)
            {
                return "通信速度設定を保存できませんでした: " + ex.Message;
            }
        }

        private List<SerialPortSetting> ReadSettings()
        {
            var result = new List<SerialPortSetting>();
            var lines = File.ReadAllLines(FilePath, FileEncoding);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)
                    || string.Equals(line.Trim(), Header, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SerialPortSetting? setting;
                if (SerialPortSetting.TryParse(line, out setting)
                    && setting != null)
                {
                    result.Add(setting);
                }
            }

            return result;
        }

        private sealed class SerialPortSetting
        {
            public string Port { get; private set; } = string.Empty;

            public string Name { get; private set; } = string.Empty;

            public int BaudRate { get; set; }

            public static SerialPortSetting FromConnection(Connection connection)
            {
                return new SerialPortSetting
                {
                    Port = connection.Port,
                    Name = connection.Name,
                    BaudRate = connection.BaudRate
                };
            }

            public static bool TryParse(
                string line,
                out SerialPortSetting? setting)
            {
                setting = null;
                var fields = ParseCsvLine(line);
                int baudRate;
                var baudRateIndex = fields.Count == 3 ? 2 : 4;
                if ((fields.Count != 3 && fields.Count != 5)
                    || !int.TryParse(
                        fields[baudRateIndex],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out baudRate))
                {
                    return false;
                }

                setting = new SerialPortSetting
                {
                    Port = fields[0],
                    Name = fields[1],
                    BaudRate = baudRate
                };
                return true;
            }

            public bool Matches(Connection connection)
            {
                return string.Equals(Port, connection.Port, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Name, connection.Name, StringComparison.OrdinalIgnoreCase);
            }

            public string ToCsvLine()
            {
                return string.Join(",",
                [
                    EscapeCsvField(Port),
                    EscapeCsvField(Name),
                    BaudRate.ToString(CultureInfo.InvariantCulture)
                ]);
            }

            private static List<string> ParseCsvLine(string line)
            {
                var fields = new List<string>();
                var field = new StringBuilder();
                var inQuotes = false;

                for (var index = 0; index < line.Length; index++)
                {
                    var character = line[index];
                    if (inQuotes)
                    {
                        if (character == '"')
                        {
                            if (index + 1 < line.Length && line[index + 1] == '"')
                            {
                                field.Append('"');
                                index++;
                            }
                            else
                            {
                                inQuotes = false;
                            }
                        }
                        else
                        {
                            field.Append(character);
                        }
                    }
                    else if (character == ',')
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                    }
                    else if (character == '"' && field.Length == 0)
                    {
                        inQuotes = true;
                    }
                    else
                    {
                        field.Append(character);
                    }
                }

                fields.Add(field.ToString());
                return fields;
            }

            private static string EscapeCsvField(string? value)
            {
                value = value ?? string.Empty;
                return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                    ? "\"" + value.Replace("\"", "\"\"") + "\""
                    : value;
            }

        }
    }
}
