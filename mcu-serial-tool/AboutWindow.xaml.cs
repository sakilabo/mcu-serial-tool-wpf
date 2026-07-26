using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;

namespace MCUSerialTool
{
    public partial class AboutWindow : Window
    {
        private const string ApplicationLicenseResource = "MCUSerialTool.License.txt";
        private const string FtdiLicenseResource = "FTD2XX_NET.License.txt";

        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "バージョン " + GetApplicationVersion();
            ApplicationLicenseTextBox.Text = ReadResource(ApplicationLicenseResource, Encoding.UTF8).Trim();
            FtdiLicenseTextBox.Text = ReadResource(FtdiLicenseResource, Encoding.GetEncoding(1252)).Trim();
        }

        private static string GetApplicationVersion()
        {
            var assembly = typeof(AboutWindow).Assembly;
            var attribute = (
                AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                assembly,
                typeof(AssemblyInformationalVersionAttribute));
            return attribute?.InformationalVersion ?? assembly.GetName().Version.ToString();
        }

        private static string ReadResource(string resourceName, Encoding encoding)
        {
            var assembly = typeof(AboutWindow).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) { return "ライセンスを読み込めませんでした。"; }
                using (var reader = new StreamReader(stream, encoding, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
