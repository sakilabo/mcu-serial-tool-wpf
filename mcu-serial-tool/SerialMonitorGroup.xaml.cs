using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MCUSerialTool
{
    public partial class SerialMonitorGroup : GroupBox
    {
        private readonly SerialPortSettingsStore _settingsStore =
            new SerialPortSettingsStore();
        private bool _isUpdatingBaudRateSelection;
        private Connection? _connection;

        public SerialMonitorGroup()
        {
            InitializeComponent();
            SubscribeToControlEvents();
            InitializeBaudRateSelector();
        }

        internal void ApplySettings(IEnumerable<Connection> connections)
        {
            _settingsStore.Apply(connections);
        }

        internal void Open(Connection connection, bool clearOutput = true)
        {
            if (connection == null) { throw new ArgumentNullException("connection"); }

            Close();
            if (clearOutput)
            {
                SerialMonitorOutput.ClearOutput();
            }

            SetSelectedBaudRate(connection.BaudRate);
            DataContext = connection;
            SubscribeToConnection(connection, true);
            try
            {
                _connection = connection;
                connection.Mode = ConnectionMode.SerialMonitor;
            }
            catch
            {
                _connection = null;
                UnsubscribeFromConnection(connection, true);
                connection.Mode = ConnectionMode.Offline;
                DataContext = null;
            }
        }

        internal void Close()
        {
            var connection = _connection;
            _connection = null;
            if (connection == null)
            {
                DataContext = null;
                return;
            }

            connection.DataReceived -= Connection_DataReceived;
            connection.Disconnected -= Connection_Disconnected;
            try
            {
                connection.Mode = ConnectionMode.Offline;
            }
            catch { /* Ignore errors. */ }
            finally
            {
                connection.PropertyChanged -= Connection_PropertyChanged;
                DataContext = null;
            }
        }

        internal void BeginProgramming(Connection connection)
        {
            if (connection == null) { throw new ArgumentNullException("connection"); }

            Close();
            _connection = connection;
            SubscribeToConnection(connection, true);
            SetSelectedBaudRate(connection.BaudRate);
            DataContext = connection;
            SerialMonitorOutput.ClearOutput();
        }

        internal void AppendInfoLine(string line)
        {
            Dispatcher.BeginInvoke(
                () => SerialMonitorOutput.Write(
                    line + Environment.NewLine));
        }

        private void SubscribeToControlEvents()
        {
            SerialMonitorBaudRate.SelectionChanged += BaudRate_SelectionChanged;
            SerialMonitorInteractiveModeButton.Click += InteractiveModeButton_Click;
            SerialMonitorDisconnectButton.Click += DisconnectButton_Click;
            SerialMonitorResetButton.Click += ResetButton_Click;
            SerialMonitorClearButton.Click += ClearButton_Click;
            SerialMonitorInput.KeyDown += Input_KeyDown;
            SerialMonitorSendButton.Click += SendButton_Click;
            SerialMonitorEscapeButton.Click += EscapeButton_Click;
        }

        private void SubscribeToConnection(
            Connection connection,
            bool includeDisconnected)
        {
            connection.PropertyChanged += Connection_PropertyChanged;
            connection.DataReceived += Connection_DataReceived;
            if (includeDisconnected)
            {
                connection.Disconnected += Connection_Disconnected;
            }
        }

        private void UnsubscribeFromConnection(
            Connection connection,
            bool includeDisconnected)
        {
            connection.PropertyChanged -= Connection_PropertyChanged;
            connection.DataReceived -= Connection_DataReceived;
            if (includeDisconnected)
            {
                connection.Disconnected -= Connection_Disconnected;
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            SerialMonitorOutput.ClearOutput();
        }

        private async void InteractiveModeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var connection = _connection;
            if (connection == null
                || connection.ConnectionType != ConnectionType.TWELITE
                || connection.Mode != ConnectionMode.SerialMonitor)
            {
                return;
            }

            SerialMonitorInteractiveModeButton.IsEnabled = false;
            try
            {
                for (var index = 0; index < 3; index++)
                {
                    if (!ReferenceEquals(_connection, connection)
                        || connection.Mode != ConnectionMode.SerialMonitor)
                    {
                        return;
                    }

                    await Task.Run(() => connection.SendSerialMonitorText("+"));
                    if (index < 2) { await Task.Delay(300); }
                }
            }
            catch (Exception)
            {
                ShowError(
                    "インタラクティブモード切り替えコマンドを送信できませんでした。");
            }
            finally
            {
                SerialMonitorInteractiveModeButton.ClearValue(IsEnabledProperty);
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendInputAsync();
        }

        private async void EscapeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var connection = _connection;
            if (connection == null
                || connection.Mode != ConnectionMode.SerialMonitor)
            {
                return;
            }

            try
            {
                await Task.Run(
                    () => connection.SendSerialMonitorText("\x1B"));
            }
            catch (Exception)
            {
                ShowError("ESCを送信できませんでした。");
            }
        }

        private async void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) { return; }

            e.Handled = true;
            await SendInputAsync();
        }

        private async Task SendInputAsync()
        {
            var connection = _connection;
            if (connection == null
                || connection.Mode != ConnectionMode.SerialMonitor)
            {
                return;
            }

            var text = SerialMonitorInput.Text;
            if (EnableEscapeCharacters.IsChecked == true)
            {
                text = ExpandEscapeCharacters(text);
            }
            text += GetEnterKeySuffix();
            try
            {
                await Task.Run(() => connection.SendSerialMonitorText(text));
                SerialMonitorInput.Clear();
            }
            catch (Exception)
            {
                ShowError("シリアルポートへ送信できませんでした。");
            }
        }

        private string GetEnterKeySuffix()
        {
            if (SendWithCrOption.IsChecked == true) { return "\r"; }
            if (SendWithCrLfOption.IsChecked == true) { return "\r\n"; }
            return string.Empty;
        }

        private static string ExpandEscapeCharacters(string text)
        {
            var expanded = new StringBuilder(text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (character != '\\' || index + 1 >= text.Length)
                {
                    expanded.Append(character);
                    continue;
                }

                var escapedCharacter = text[index + 1];
                switch (escapedCharacter)
                {
                    case 'r':
                        expanded.Append('\r');
                        index++;
                        break;
                    case 'n':
                        expanded.Append('\n');
                        index++;
                        break;
                    case 't':
                        expanded.Append('\t');
                        index++;
                        break;
                    case '0':
                        expanded.Append('\0');
                        index++;
                        break;
                    case '\\':
                        expanded.Append('\\');
                        index++;
                        break;
                    case 'x':
                        if (TryAppendHexEscape(text, ref index, expanded))
                        {
                            break;
                        }
                        expanded.Append('\\');
                        break;
                    default:
                        expanded.Append('\\');
                        break;
                }
            }
            return expanded.ToString();
        }

        private static bool TryAppendHexEscape(
            string text,
            ref int escapeStart,
            StringBuilder expanded)
        {
            if (escapeStart + 3 >= text.Length
                || !Uri.IsHexDigit(text[escapeStart + 2])
                || !Uri.IsHexDigit(text[escapeStart + 3]))
            {
                return false;
            }

            var value = Convert.ToByte(
                text.Substring(escapeStart + 2, 2),
                16);
            expanded.Append((char)value);
            escapeStart += 3;
            return true;
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var connection = _connection;
            if (connection == null) { return; }

            var result = MessageBox.Show(
                Window.GetWindow(this),
                connection.ResetConfirmationMessage,
                "マイコンアプリ更新ツール",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) { return; }

            SerialMonitorResetButton.IsEnabled = false;
            try
            {
                await Task.Run(() => connection.ResetMCU());
            }
            catch (Exception)
            {
                ShowError("マイコンをリセットできませんでした。");
            }
            finally
            {
                SerialMonitorResetButton.ClearValue(IsEnabledProperty);
            }
        }

        private void BaudRate_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isUpdatingBaudRateSelection || _connection == null) { return; }

            var selectedValue = SerialMonitorBaudRate.SelectedItem;
            if (!(selectedValue is int)) { return; }

            var baudRate = (int)selectedValue;
            var connection = _connection;
            if (connection.BaudRate == baudRate) { return; }

            Close();
            connection.BaudRate = baudRate;
            _settingsStore.Save(connection);
            Open(connection, false);
        }

        private void Connection_DataReceived(
            object sender,
            ConnectionDataReceivedEventArgs e)
        {
            var connection = sender as Connection;
            if (connection == null) { return; }

            Dispatcher.BeginInvoke(
                () => AppendReceivedText(connection, e.Text));
        }

        private void Connection_Disconnected(object sender, EventArgs e)
        {
            var connection = sender as Connection;
            if (connection == null) { return; }

            Dispatcher.BeginInvoke(
                () => HandleDisconnected(connection));
        }

        private void Connection_PropertyChanged(
            object sender,
            PropertyChangedEventArgs e)
        {
            var connection = sender as Connection;
            if (connection == null || e.PropertyName != "Mode") { return; }

            Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(DataContext, connection)) { return; }

                if (connection.Mode != ConnectionMode.Offline)
                {
                    SerialMonitorOutput.ClearOutput();
                }
            });
        }

        private void HandleDisconnected(Connection connection)
        {
            if (!ReferenceEquals(_connection, connection)
                || connection.Mode != ConnectionMode.Offline)
            {
                return;
            }

            UnsubscribeFromConnection(connection, true);
            _connection = null;
            DataContext = null;
        }

        private void AppendReceivedText(Connection source, string text)
        {
            if (!ReferenceEquals(_connection, source)) { return; }

            SerialMonitorOutput.Write(text);
        }

        private void InitializeBaudRateSelector()
        {
            _isUpdatingBaudRateSelection = true;
            try
            {
                SerialMonitorBaudRate.ItemsSource =
                    Connection.SupportedBaudRates
                        .OrderByDescending(baudRate => baudRate);
                SerialMonitorBaudRate.SelectedItem =
                    Connection.DefaultBaudRate;
            }
            finally
            {
                _isUpdatingBaudRateSelection = false;
            }
        }

        private void SetSelectedBaudRate(int baudRate)
        {
            _isUpdatingBaudRateSelection = true;
            try
            {
                SerialMonitorBaudRate.SelectedItem = baudRate;
            }
            finally
            {
                _isUpdatingBaudRateSelection = false;
            }
        }

        private void ShowError(string message)
        {
            var owner = Window.GetWindow(this);
            if (owner == null)
            {
                MessageBox.Show(
                    message,
                    "マイコンアプリ更新ツール",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                owner,
                message,
                "マイコンアプリ更新ツール",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
