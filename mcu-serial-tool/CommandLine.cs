using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MCUSerialTool
{
    // コマンドラインからファームウェアの書き込みまたはリセットを行い、再起動後のシリアル出力を受信する
    internal static class CommandLine
    {
        private const string Usage =
            "使い方: write --firmware <ファイル> [--port <COMn>] [--verify] [--monitor <秒>]"
            + "\r\n"
            + "        reset [--port <COMn>] [--monitor <秒>]";

        private const int ExitSuccess = 0;
        private const int ExitFailure = 1;
        private const int ExitUsage = 2;

        private const int StdOutputHandle = -11;
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int stdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        internal static bool IsCommand(string[] args)
        {
            return args.Length > 0 && (args[0] == "write" || args[0] == "reset");
        }

        internal static async Task<int> RunAsync(string[] args)
        {
            // WinExe は標準出力を持たずに起動される場合があるので、親のコンソールに接続する
            var hasStdOutput = GetStdHandle(StdOutputHandle) != IntPtr.Zero;
            if (!hasStdOutput) { AttachConsole(AttachParentProcess); }
            var output = OpenWriter(
                Console.IsOutputRedirected,
                Console.OpenStandardOutput,
                Console.Out);
            var error = OpenWriter(
                Console.IsErrorRedirected,
                Console.OpenStandardError,
                Console.Error);

            var options = Options.Parse(args);
            if (options == null)
            {
                error.WriteLine(Usage);
                return ExitUsage;
            }

            try
            {
                if (options.Command == "reset") { return await ResetAsync(options, output, error); }
                return await WriteAsync(options, output, error);
            }
            catch (Exception ex)
            {
                error.WriteLine("エラー: " + ex.Message);
                return ExitFailure;
            }
        }

        private static async Task<int> WriteAsync(
            Options options,
            TextWriter output,
            TextWriter error)
        {
            var firmware = Firmware.Load(options.FirmwarePath!);
            if (firmware.Type == FirmwareType.UNKNOWN)
            {
                error.WriteLine("ファームウェアを読み込めません: " + options.FirmwarePath);
                return ExitFailure;
            }

            var discovery = Connection.Discovery();
            if (discovery.Warning != null) { error.WriteLine("警告: " + discovery.Warning); }
            var connections = discovery.Connections
                .Where(connection => options.Port == null || string.Equals(
                    connection.Port,
                    options.Port,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (connections.Count == 0)
            {
                error.WriteLine("ポートがありません。");
                return ExitFailure;
            }
            if (connections.Count > 1)
            {
                var ports = string.Join(", ", connections.Select(connection => connection.Port));
                error.WriteLine("ポートが複数あります。--port で指定してください: " + ports);
                return ExitFailure;
            }

            var target = connections[0];
            var mcuType = target.GetMCUType(firmware);
            if (mcuType == null)
            {
                var portName = target.Port + " (" + target.Name + ")";
                error.WriteLine(portName + " には書き込めません: " + firmware.FilePath);
                return ExitFailure;
            }
            target.SelectMCU(mcuType);
            target.DataReceived += (sender, e) => output.Write(e.Text);
            error.WriteLine(target.Port + " (" + target.Name + ") に書き込みます: " + firmware.FilePath);
            try
            {
                var result = await target.WriteFirmwareAsync(
                    firmware,
                    options.Verify,
                    error.WriteLine);
                if (!result.Succeeded)
                {
                    error.WriteLine("エラー: " + result.ErrorMessage);
                    return ExitFailure;
                }
                await Task.Delay(TimeSpan.FromSeconds(options.MonitorSeconds));
                return ExitSuccess;
            }
            finally
            {
                target.Mode = ConnectionMode.Offline;
                output.Flush();
            }
        }

        private static async Task<int> ResetAsync(
            Options options,
            TextWriter output,
            TextWriter error)
        {
            var discovery = Connection.Discovery();
            if (discovery.Warning != null) { error.WriteLine("警告: " + discovery.Warning); }
            var connections = discovery.Connections
                .Where(connection => options.Port == null || string.Equals(
                    connection.Port,
                    options.Port,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (connections.Count == 0)
            {
                error.WriteLine("ポートがありません。");
                return ExitFailure;
            }
            if (connections.Count > 1)
            {
                var ports = string.Join(", ", connections.Select(connection => connection.Port));
                error.WriteLine("ポートが複数あります。--port で指定してください: " + ports);
                return ExitFailure;
            }

            var target = connections[0];
            target.DataReceived += (sender, e) => output.Write(e.Text);
            error.WriteLine(target.Port + " (" + target.Name + ") をリセットします。");
            try
            {
                target.Mode = ConnectionMode.SerialMonitor;
                await Task.Run(() => target.ResetMCU());
                await Task.Delay(TimeSpan.FromSeconds(options.MonitorSeconds));
                return ExitSuccess;
            }
            finally
            {
                target.Mode = ConnectionMode.Offline;
                output.Flush();
            }
        }

        // リダイレクトされている場合は UTF-8 で出力し、コンソールの場合はコンソールの設定に従う
        private static TextWriter OpenWriter(
            bool redirected,
            Func<Stream> openStream,
            TextWriter consoleWriter)
        {
            if (!redirected) { return TextWriter.Synchronized(consoleWriter); }
            var writer = new StreamWriter(openStream(), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            return TextWriter.Synchronized(writer);
        }

        private sealed class Options
        {
            private Options(
                string command,
                string? firmwarePath,
                string? port,
                bool verify,
                int monitorSeconds)
            {
                Command = command;
                FirmwarePath = firmwarePath;
                Port = port;
                Verify = verify;
                MonitorSeconds = monitorSeconds;
            }

            public string Command { get; private set; }

            public string? FirmwarePath { get; private set; }

            public string? Port { get; private set; }

            public bool Verify { get; private set; }

            public int MonitorSeconds { get; private set; }

            public static Options? Parse(string[] args)
            {
                var command = args[0];
                string? firmwarePath = null;
                string? port = null;
                var verify = false;
                var monitorSeconds = 0;
                var queue = new Queue<string>(args.Skip(1));
                while (queue.Count > 0)
                {
                    var name = queue.Dequeue();
                    if (name == "--verify")
                    {
                        verify = true;
                        continue;
                    }
                    if (queue.Count == 0) { return null; }
                    var value = queue.Dequeue();
                    switch (name)
                    {
                        case "--firmware":
                            firmwarePath = value;
                            break;
                        case "--port":
                            port = value;
                            break;
                        case "--monitor":
                            if (!int.TryParse(value, out monitorSeconds)) { return null; }
                            if (monitorSeconds < 0) { return null; }
                            break;
                        default:
                            return null;
                    }
                }
                if (command == "write" && firmwarePath == null) { return null; }
                if (command == "reset" && (firmwarePath != null || verify)) { return null; }
                return new Options(command, firmwarePath, port, verify, monitorSeconds);
            }
        }
    }
}
