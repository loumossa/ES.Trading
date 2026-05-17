using System;
using System.IO;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Append-only file log for the AddOn. Mirrors anything sent through NT8's
    /// Output tab so a silent initialization failure leaves a trail the user
    /// can find without keeping the Output tab open.
    ///
    /// Path: <c>Documents\NinjaTrader 8\ES.Trading\logs\addon.log</c>.
    /// Writes are best-effort — any IO failure is swallowed so logging never
    /// breaks the AddOn itself.
    /// </summary>
    public static class AddonLog
    {
        private static readonly object _gate = new object();

        public static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NinjaTrader 8", "ES.Trading", "logs", "addon.log");

        public static void Info(string message)  => Write("INFO",  message);
        public static void Warn(string message)  => Write("WARN",  message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception ex)
            => Write("ERROR", message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}";

                lock (_gate)
                    File.AppendAllText(path, line);
            }
            catch
            {
                // Best effort — never let logging break the AddOn.
            }
        }
    }
}
