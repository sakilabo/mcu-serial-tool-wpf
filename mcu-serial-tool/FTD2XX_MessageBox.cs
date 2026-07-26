#if DEBUG
namespace FTD2XX_NET
{
    /// <summary>
    /// FTD2XX_NET 向けの MessageBox 代替クラス。
    /// Debug 構成の時に MessageBox が参照されてビルドエラーになることを回避します。
    /// </summary>
    internal static class MessageBox
    {
        public static void Show(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
#endif
