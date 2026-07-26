using System;

namespace MCUSerialTool
{
    internal sealed class ConnectionDataReceivedEventArgs : EventArgs
    {
        public ConnectionDataReceivedEventArgs(string text)
        {
            Text = text;
        }
        public string Text { get; private set; }
    }
}
