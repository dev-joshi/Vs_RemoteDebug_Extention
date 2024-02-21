namespace RemoteDebug
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Community.VisualStudio.Toolkit;

    public static class OutputWindowLogger
    {
        private static readonly Lazy<TextWriter> OutputWriter = new(() => InitAsync().Result);
        
        private static async Task<TextWriter> InitAsync()
        {
            return await VsUtils.GetOutputWindowsWriterAsync("Dev Tools");
        }

        private static void Log(string level, string message)
        {
            try
            {
                var log = $"{DateTime.Now:o}\t{level}\t{message}";
                OutputWriter.Value.WriteLine(log);
                OutputWriter.Value.Flush();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public static void Info(string message)
        {
            Log("INF", message);
        }

        public static void Warn(string message)
        {
            Log("WAR", message);
        }

        public static void Warn(Exception ex, string message)
        {
            Log("WAR", $"{message}, Exception : {ex.Message}");
        }

        public static void Err(string message)
        {
            Log("ERR", message);
        }

        public static void Err(Exception ex, string message)
        {
            Log("ERR", $"{message}, Exception : {ex.Message}");
        }
    }
}