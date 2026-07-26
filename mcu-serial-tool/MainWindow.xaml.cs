using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MCUSerialTool
{
    public partial class MainWindow : Window
    {
        private Firmware? _firmware;

        private DataGrid PortList
        {
            get { return PortPanel.PortList; }
        }

        private Button RefreshPortsButton
        {
            get { return PortPanel.RefreshPortsButton; }
        }

        private TextBox FirmwarePath
        {
            get { return FirmwarePanel.FirmwarePath; }
        }

        private Button WriteButton
        {
            get { return FirmwarePanel.WriteButton; }
        }

        private TextBlock FirmwareTypeText
        {
            get { return FirmwarePanel.FirmwareTypeText; }
        }

        private CheckBox VerifyFirmwareCheckBox
        {
            get { return FirmwarePanel.VerifyFirmwareCheckBox; }
        }

        public MainWindow()
        {
            InitializeComponent();
            SubscribeToControlEvents();
            RefreshPorts();
        }

        private void SubscribeToControlEvents()
        {
            RefreshPortsButton.Click += RefreshPorts_Click;
            PortList.SelectionChanged += PortList_SelectionChanged;
            PortList.GotKeyboardFocus += PortList_GotKeyboardFocus;
            PortList.PreviewKeyDown += PortList_PreviewKeyDown;
            PortList.MouseDoubleClick += PortList_MouseDoubleClick;

            FirmwarePanel.BrowseButton.Click += Browse_Click;
            WriteButton.Click += Write_Click;
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void RefreshPorts_Click(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void PortList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FirmwarePanel.DataContext = PortList.SelectedItem;
            UpdateWriteButtonState();
        }

        private void RefreshPorts()
        {
            SerialMonitor.Close();

            if (PortList.Items.OfType<Connection>().Any(item => item.IsWriting))
            {
                return;
            }

            var result = Connection.Discovery();
            var connections = result.Connections;
            SerialMonitor.ApplySettings(connections);

            PortList.ItemsSource = connections;
            PortList.SelectedIndex = -1;
            UpdateWriteButtonState();
        }

        private void PortList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            var row = ItemsControl.ContainerFromElement(PortList, source) as DataGridRow;
            var port = row?.Item as Connection;
            if (port == null) { return; }

            OpenSerialMonitor(port);
            e.Handled = true;
        }

        private void PortList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!ReferenceEquals(e.NewFocus, PortList)
                || !(PortList.SelectedItem is Connection))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                () => FocusSelectedPortCell(),
                DispatcherPriority.Input);
        }

        private void FocusSelectedPortCell()
        {
            var port = PortList.SelectedItem as Connection;
            if (port == null || !ReferenceEquals(Keyboard.FocusedElement, PortList)) { return; }

            FocusPortCell(port);
        }

        private void FocusPortCell(Connection port)
        {
            var currentCell = PortList.CurrentCell;
            var column = currentCell.IsValid && ReferenceEquals(currentCell.Item, port)
                ? currentCell.Column
                : PortList.Columns.Count > 1
                    ? PortList.Columns[1]
                    : PortList.Columns[0];

            PortList.ScrollIntoView(port);
            PortList.CurrentCell = new DataGridCellInfo(port, column);
            PortList.UpdateLayout();

            var content = column.GetCellContent(port);
            var cell = FindVisualParentOrSelf<DataGridCell>(content);
            cell?.Focus();
        }

        private void PortList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Up || e.Key == Key.Down)
                && !PortList.CurrentCell.IsValid)
            {
                e.Handled = true;
                if (PortList.Items.Count > 0)
                {
                    var firstPort = PortList.Items[0] as Connection;
                    if (firstPort != null)
                    {
                        PortList.SelectedIndex = 0;
                        FocusPortCell(firstPort);
                    }
                }

                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var port = PortList.SelectedItem as Connection;
                if (port != null)
                {
                    OpenSerialMonitor(port);
                }

                return;
            }

            if (e.Key != Key.Tab) { return; }

            e.Handled = true;
            var direction = (Keyboard.Modifiers & ModifierKeys.Shift) == 0
                ? FocusNavigationDirection.Next
                : FocusNavigationDirection.Previous;
            var tabNavigation = KeyboardNavigation.GetTabNavigation(PortList);
            KeyboardNavigation.SetTabNavigation(PortList, KeyboardNavigationMode.None);
            try
            {
                PortList.MoveFocus(new TraversalRequest(direction));
            }
            finally
            {
                KeyboardNavigation.SetTabNavigation(PortList, tabNavigation);
            }
        }

        private static T? FindVisualParentOrSelf<T>(DependencyObject? element)
            where T : DependencyObject
        {
            while (element != null)
            {
                var typedElement = element as T;
                if (typedElement != null) { return typedElement; }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private void OpenSerialMonitor(Connection connection)
        {
            if (PortList.Items.OfType<Connection>().Any(item => item.IsWriting)) { return; }

            SerialMonitor.Open(connection);
        }

        private void Window_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = GetDroppedFilePath(e.Data) == null
                ? DragDropEffects.None
                : DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_PreviewDrop(object sender, DragEventArgs e)
        {
            var filePath = GetDroppedFilePath(e.Data);
            if (filePath != null)
            {
                SetFirmwareFile(filePath);
                BringWindowToFront();
            }

            e.Handled = true;
        }

        private static string? GetDroppedFilePath(IDataObject? data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) { return null; }

            var filePaths = data.GetData(DataFormats.FileDrop) as string[];
            if (filePaths == null) { return null; }

            foreach (var filePath in filePaths)
            {
                if (IsFirmwareFile(filePath)) { return filePath; }
            }

            return null;
        }

        private void SetFirmwareFile(string filePath)
        {
            if (!IsFirmwareFile(filePath)) { return; }

            _firmware = Firmware.Load(filePath);
            FirmwarePath.Text = Path.GetFileName(filePath);
            UpdateFirmwareTypeDisplay();
            UpdateWriteButtonState();
        }

        private void BringWindowToFront()
        {
            if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; }

            Topmost = true;
            Activate();
            Topmost = false;
            Focus();
        }

        private static bool IsFirmwareFile(string filePath)
        {
            return File.Exists(filePath)
                && string.Equals(
                    Path.GetExtension(filePath),
                    ".bin",
                    StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateWriteButtonState()
        {
            if (PortList.Items.OfType<Connection>().Any(item => item.IsWriting))
            {
                WriteButton.IsEnabled = false;
                return;
            }

            var port = PortList.SelectedItem as Connection;
            WriteButton.IsEnabled = _firmware != null
                && port != null
                && port.SupportsFirmware(_firmware);
        }

        private void UpdateFirmwareTypeDisplay()
        {
            FirmwareTypeText.Text = _firmware == null
                ? "ファームウェア種別: 未選択"
                : "ファームウェア種別: "
                    + _firmware.Type.ToString().Replace('_', ' ');
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "書き込みファイルを選択",
                Filter = "バイナリファイル (*.bin)|*.bin",
                DefaultExt = ".bin"
            };

            if (dialog.ShowDialog(this) == true)
            {
                SetFirmwareFile(dialog.FileName);
            }
        }

        private async void Write_Click(object sender, RoutedEventArgs e)
        {
            var connection = PortList.SelectedItem as Connection;
            var firmware = _firmware;
            if (firmware == null
                || connection == null
                || !connection.SupportsFirmware(firmware)
                || PortList.Items.OfType<Connection>().Any(item => item.IsWriting))
            {
                return;
            }

            var mcuType = connection.GetMCUType(firmware);
            if (mcuType == null) { return; }
            connection.SelectMCU(mcuType);
            var verify = VerifyFirmwareCheckBox.IsChecked == true;

            SerialMonitor.BeginProgramming(connection);
            var writeTask = connection.WriteFirmwareAsync(
                firmware,
                verify,
                SerialMonitor.AppendInfoLine);
            RefreshPortsButton.IsEnabled = false;
            UpdateWriteButtonState();
            var succeeded = false;
            try
            {
                var result = await writeTask;
                if (result.Succeeded)
                {
                    succeeded = true;
                }
                else
                {
                    SerialMonitor.AppendInfoLine(
                        "エラー: " + result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                SerialMonitor.AppendInfoLine("エラー: " + ex.Message);
            }
            finally
            {
                RefreshPortsButton.IsEnabled = true;
                UpdateFirmwareTypeDisplay();
                UpdateWriteButtonState();
            }

            if (!succeeded)
            {
                MessageBox.Show(
                    this,
                    "書き込みできませんでした。",
                    "マイコンアプリ更新ツール",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (PortList.Items.OfType<Connection>().Any(item => item.IsWriting))
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "ファームウェアの書き込み中は終了できません。\n"
                        + "書き込みが完了するまでお待ちください。",
                    "マイコンアプリ更新ツール",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            SerialMonitor.Close();
            base.OnClosed(e);
        }
    }
}
