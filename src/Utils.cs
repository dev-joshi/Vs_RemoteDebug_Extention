namespace RemoteDebug
{
    using Microsoft.VisualStudio.Threading;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Win32;

    internal static class Utils
    {
        public static bool SetRegValue(RegistryHive hive, string path, string name, object value, RegistryView view = RegistryView.Registry64)
        {
            OutputWindowLogger.Info(@$"attempting to set reg {hive}\{path} name {name} value {value}");

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var subKey = baseKey.OpenSubKey(path, true);
                subKey.SetValue(name, value);
                OutputWindowLogger.Info(@$"Set reg {hive}\{path} name {name} to value {value}");
                return true;
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Failed to set reg");
            }

            return false;
        }

        public static async Task<(bool, int)> RunProcessAsync(string file, string arguments = null, int timeout = 30000)
        {
            OutputWindowLogger.Info($"Starting to run command : {file} with args: {arguments}");

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = file,
                    Arguments = arguments,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Verb = "runas"
                });

                if(process == null)
                {
                    OutputWindowLogger.Err("Failed to run command");
                    return (false, -1);
                }

                var standardOutTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!process.StandardOutput.EndOfStream)
                        {
                            OutputWindowLogger.Info($"Process {process.Id} output: {await process.StandardOutput.ReadLineAsync()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputWindowLogger.Err(ex, "stdout stream error");
                    }
                });

                var standardErrorTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!process.StandardError.EndOfStream)
                        {
                            OutputWindowLogger.Info($"Process {process.Id} error output: {await process.StandardError.ReadLineAsync()}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OutputWindowLogger.Err(ex, "stderr stream error");
                    }
                });

                OutputWindowLogger.Info($"Waiting for command to complete with timeout {timeout} milliseconds");

                var exitCode = await process.WaitForExitAsync(new CancellationTokenSource(timeout).Token);

                await Task.WhenAll(standardOutTask, standardErrorTask);

                return (true, exitCode);
            }
            catch (TaskCanceledException)
            {
                OutputWindowLogger.Err($"Timed out running: {file} with args: {arguments}");
                return (false, -1);
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Failed to run command");
                return (false, -1);
            }
        }
    }
}
