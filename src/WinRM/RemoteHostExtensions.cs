namespace RemoteDebug.WinRM
{
    using Microsoft.Management.Infrastructure;
    using Microsoft.Win32;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Security.AccessControl;
    using System.Security.Principal;
    using System.Text;
    using System.Threading.Tasks;

    internal static class RemoteHostExtensions
    {
        internal static async Task CheckConnectionAsync(this RemoteHost remoteHost, Func<Task> onSuccess, Func<string, Task> onFailure)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to connect to {remoteHost.Hostname}");

                if (!Utils.SetRegValue(
                        RegistryHive.LocalMachine,
                        @"Software\Policies\Microsoft\Windows\WinRM\Client",
                        "AllowUnencryptedTraffic",
                        1))
                {
                    await onFailure("Failed while setting up GPO registry for remote connection");
                    return;
                }

                if (!(await Utils.RunProcessAsync("cmd.exe", "/c winrm quickconfig -quiet -force", 30000)).Item1
                    || !(await Utils.RunProcessAsync("cmd.exe", $"/c winrm set winrm/config/client @{{AllowUnencrypted = \"true\";TrustedHosts = \"{remoteHost.Hostname}\"}}")).Item1)
                {
                    OutputWindowLogger.Err("Failed while running winrm setup commands");
                    await onFailure("Failed while setting up for remote connection");
                    return;
                }
                
                var result = remoteHost.CimSession.GetInstanceAsync(@"root\cimv2", new CimInstance("Win32_OperatingSystem", @"root\cimv2"));
                result.Subscribe(new CimInstanceObserver(async (instance) =>
                {
                    var name = (string)instance.CimInstanceProperties.FirstOrDefault(x => x.Name == "CSName")?.Value;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        OutputWindowLogger.Info("Connection successful !");
                        remoteHost.ComputerName = name;
                        await onSuccess();
                    }
                    else
                    {
                        OutputWindowLogger.Err($"Remote computer name not found, Failed to connect to {remoteHost.Hostname}");
                        await onFailure("Failed to connect to remote host");
                    }

                    instance.Dispose();
                },
                async (ex) =>
                {
                    OutputWindowLogger.Err(ex, $"Failed to connect to {remoteHost.Hostname}");
                    await onFailure("Failed to connect to remote host");
                }));
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, $"Failed while connecting to {remoteHost.Hostname}");
                await onFailure("Failed to connect to remote host");
            }
        }

        internal static bool RunCommand(this RemoteHost remoteHost, string commandLine)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to Run Command \"{commandLine}\" on {remoteHost.Hostname}");

                using var startup = new CimInstance("Win32_ProcessStartup", @"root\cimv2");
                startup.CimInstanceProperties.Add(CimProperty.Create("CreateFlags", (uint)CREATE_NEW_CONSOLE, CimType.UInt32, CimFlags.Property));
                startup.CimInstanceProperties.Add(CimProperty.Create("ShowWindow", (uint)SW_SHOW, CimType.UInt16, CimFlags.Property));
                startup.CimInstanceProperties.Add(CimProperty.Create("WinstationDesktop", string.Empty, CimType.String, CimFlags.Property));

                using var instance = new CimInstance("Win32_Process", @"root\cimv2");
                var cimMethodParameters = new CimMethodParametersCollection();
                cimMethodParameters.Add(CimMethodParameter.Create("CommandLine", commandLine, CimType.String, CimFlags.In));
                cimMethodParameters.Add(CimMethodParameter.Create("ProcessStartupInformation", startup, CimType.Instance, CimFlags.In));

                var result = remoteHost.CimSession.InvokeMethod(instance, "Create", cimMethodParameters);
                var returnCode = (uint)result.ReturnValue.Value;

                if (returnCode == 0)
                {
                    OutputWindowLogger.Info("Remote command run successful !");
                    return true;
                }

                OutputWindowLogger.Err($"Remote command run failed with code : {returnCode}");
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Remote command failed");
            }

            return false;
        }

        internal static bool CreateSharedDirectory(this RemoteHost remoteHost, string path)
        {
            try
            {
                var dirName = path.Split('\\').Last();

                OutputWindowLogger.Info($"Attempting to Create Remote Shared Directory \\\\{remoteHost.Hostname}\\{dirName}");

                using var instance = new CimInstance("Win32_Share", @"root\cimv2");

                SecurityIdentifier worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                byte[] worldSidArray = new byte[worldSid.BinaryLength];
                worldSid.GetBinaryForm(worldSidArray, 0);

                SecurityIdentifier anonymousSid = new SecurityIdentifier(WellKnownSidType.AnonymousSid, null);
                byte[] anonymousSidArray = new byte[anonymousSid.BinaryLength];
                anonymousSid.GetBinaryForm(anonymousSidArray, 0);

                using var trustee1 = new CimInstance("Win32_Trustee", @"root\cimv2");
                trustee1.CimInstanceProperties.Add(CimProperty.Create("SID", worldSidArray, CimType.UInt8Array, CimFlags.Property));
                trustee1.CimInstanceProperties.Add(CimProperty.Create("Name", "Everyone", CimType.String, CimFlags.Property));

                using var trustee2 = new CimInstance("Win32_Trustee", @"root\cimv2");
                trustee2.CimInstanceProperties.Add(CimProperty.Create("SID", anonymousSidArray, CimType.UInt8Array, CimFlags.Property));
                trustee2.CimInstanceProperties.Add(CimProperty.Create("Name", "ANONYMOUS LOGON", CimType.String, CimFlags.Property));

                using var ace1 = new CimInstance("Win32_ACE", @"root\cimv2");
                ace1.CimInstanceProperties.Add(CimProperty.Create("AccessMask", (uint)FileSystemRights.FullControl, CimType.UInt32, CimFlags.Property));
                ace1.CimInstanceProperties.Add(CimProperty.Create("AceFlags", (uint)(AceFlags.ObjectInherit | AceFlags.ContainerInherit), CimType.UInt32, CimFlags.Property));
                ace1.CimInstanceProperties.Add(CimProperty.Create("AceType", (uint)(AceType.AccessAllowed), CimType.UInt32, CimFlags.Property));
                ace1.CimInstanceProperties.Add(CimProperty.Create("Trustee", trustee1, CimType.Instance, CimFlags.Property));

                using var ace2 = new CimInstance("Win32_ACE", @"root\cimv2");
                ace2.CimInstanceProperties.Add(CimProperty.Create("AccessMask", (uint)FileSystemRights.FullControl, CimType.UInt32, CimFlags.Property));
                ace2.CimInstanceProperties.Add(CimProperty.Create("AceFlags", (uint)(AceFlags.ObjectInherit | AceFlags.ContainerInherit), CimType.UInt32, CimFlags.Property));
                ace2.CimInstanceProperties.Add(CimProperty.Create("AceType", (uint)(AceType.AccessAllowed), CimType.UInt32, CimFlags.Property));
                ace2.CimInstanceProperties.Add(CimProperty.Create("Trustee", trustee2, CimType.Instance, CimFlags.Property));

                using var security_descriptor = new CimInstance("Win32_SecurityDescriptor", @"root\cimv2");
                security_descriptor.CimInstanceProperties.Add(CimProperty.Create("ControlFlags", (uint)4, CimType.UInt32, CimFlags.Property));
                security_descriptor.CimInstanceProperties.Add(CimProperty.Create("DACL", new CimInstance[] { ace1, ace2 }, CimType.InstanceArray, CimFlags.Property));
                security_descriptor.CimInstanceProperties.Add(CimProperty.Create("Group", trustee1, CimType.Instance, CimFlags.Property));

                using var cimMethodParameters = new CimMethodParametersCollection();
                cimMethodParameters.Add(CimMethodParameter.Create("Path", path, CimType.String, CimFlags.In));
                cimMethodParameters.Add(CimMethodParameter.Create("Name", dirName, CimType.String, CimFlags.In));
                cimMethodParameters.Add(CimMethodParameter.Create("Type", 0, CimType.UInt32, CimFlags.In));
                cimMethodParameters.Add(CimMethodParameter.Create("Access", security_descriptor, CimType.Instance, CimFlags.In));

                var result = remoteHost.CimSession.InvokeMethod(instance, "Create", cimMethodParameters);
                var returnCode = (uint)result.ReturnValue.Value;

                if (returnCode == 0 || returnCode == 22)
                {
                    OutputWindowLogger.Info("Remote Shared Directory creation successful !");
                    return true;
                }

                OutputWindowLogger.Err($"Remote Shared Directory creation failed with code : {returnCode}");
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Remote Shared Directory creation failed");
            }

            return false;
        }

        internal static bool CopyFiles(this RemoteHost remoteHost, string sharedDirectory, string sourceDirectory)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to copy files from {sourceDirectory} to {sharedDirectory}");

                if(!ConnectToShare(sharedDirectory, remoteHost.Username, remoteHost.Password))
                {
                    return false;
                }

                var newDirectory = Path.Combine(sharedDirectory, new DirectoryInfo(sourceDirectory).Name);
                Directory.CreateDirectory(newDirectory);

                var filesToBeCopied = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);

                foreach (var file in filesToBeCopied)
                {
                    var destFile = Path.Combine(newDirectory, Path.GetFileName(file));
                    OutputWindowLogger.Info($"Copy file {file} to {destFile}");
                    File.Copy(file, destFile, true);
                }

                _ = DisconnectFromShare(sharedDirectory);

                OutputWindowLogger.Info("copy files successful !");
                return true;
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Failed to copy files");
            }

            return false;
        }

        internal static bool CopyFoldersToSamePath(this RemoteHost remoteHost, string sharedDirectory, IEnumerable<string> folders)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to copy {folders.Count()} folders to same path on {remoteHost.Hostname}");

                if (!ConnectToShare(sharedDirectory, remoteHost.Username, remoteHost.Password))
                {
                    return false;
                }

                var zips = Path.Combine(sharedDirectory, "zips");
                Directory.CreateDirectory(zips);

                var transferComplete = true;

                Parallel.ForEach(folders, folder =>
                {
                    OutputWindowLogger.Info($"zipping {folder}");
                    var parentFolder = Path.GetDirectoryName(folder);
                    var zipName = $"{Path.GetFileName(folder)}{Guid.NewGuid()}.zip";
                    var zipPath = Path.Combine(Path.GetDirectoryName(folder), zipName);
                    var zipPathOnRemote = Path.Combine(zips, zipName);

                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }

                    ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, true);
                    OutputWindowLogger.Info($"zipped {folder} into {zipPath}");
                    File.Copy(zipPath, zipPathOnRemote, true);
                    OutputWindowLogger.Info($"copied zip {zipName} to remote");
                    OutputWindowLogger.Info($"running commmand on remote to extract {zipName} to {parentFolder}");
                    if (!remoteHost.RunCommand($"powershell.exe -nologo -noprofile -command \"& {{ Add-Type -A 'System.IO.Compression.FileSystem'; [System.IO.Compression.ZipFile]::ExtractToDirectory('{zipPathOnRemote}', '{parentFolder}'); }}\""))
                    {
                        OutputWindowLogger.Err($"failed to run command to extract {zipName}");
                        transferComplete = false;
                    }
                });

                _ = DisconnectFromShare(sharedDirectory);

                if (transferComplete)
                {
                    OutputWindowLogger.Info("copy folders successful !");
                    return true;
                }
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Failed to copy folder");
            }

            return false;
        }

        internal static bool FindInstalledApp(this RemoteHost remoteHost, string appName, Version version)
        {
            try
            {
                OutputWindowLogger.Info($"Finding app {appName} version {version} on {remoteHost.Hostname}");

                var highestVersion = new Version();

                var queryInstances = remoteHost.CimSession.QueryInstances(@"root\cimv2", "WQL", @$"select * from Win32_Product where Name = '{appName}'");
                foreach (var instance in queryInstances)
                {
                    var versionString = (string)instance.CimInstanceProperties["Version"]?.Value;
                    if (!string.IsNullOrWhiteSpace(versionString)
                        && Version.TryParse(versionString, out var foundVersion)
                        && foundVersion >= version)
                    {
                        highestVersion = foundVersion;
                    }
                }

                if (highestVersion >= version)
                {
                    OutputWindowLogger.Info($"Found app {appName} version {highestVersion} on {remoteHost.Hostname}");
                    return true;
                }

                OutputWindowLogger.Warn("Required app version not installed");
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Failed to find required app");
            }

            return false;
        }

        private static bool ConnectToShare(string path, string username, string password)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to connect to {path}");

                NETRESOURCE nr = new NETRESOURCE();
                nr.dwType = RESOURCETYPE_DISK;
                nr.lpRemoteName = path;

                int errorCode = WNetUseConnection(IntPtr.Zero, nr, password, username, 0, null, null, null);

                if (errorCode == 0)
                {
                    OutputWindowLogger.Info($"Connected to {path}");
                    return true;
                }

                OutputWindowLogger.Err($"connection to shared directory failed with error code : {errorCode}");
            }
            catch(Exception ex)
            {
                OutputWindowLogger.Err(ex, "connection to shared directory failed");
            }

            return false;
        }

        private static bool DisconnectFromShare(string path)
        {
            try
            {
                OutputWindowLogger.Info($"Attempting to disconnect from {path}");

                NETRESOURCE nr = new NETRESOURCE();
                nr.dwType = RESOURCETYPE_DISK;
                nr.lpRemoteName = path;

                int errorCode = WNetCancelConnection(path, false);

                if (errorCode == 0)
                {
                    OutputWindowLogger.Info($"disconnected from {path}");
                    return true;
                }

                OutputWindowLogger.Warn($"disconnection from shared directory failed with error code : {errorCode}");
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Warn(ex, "disconnection from shared directory failed");
            }

            return false;
        }

        [DllImport("Mpr.dll")]
        private static extern int WNetUseConnection(
            IntPtr hwndOwner,
            NETRESOURCE lpNetResource,
            string lpPassword,
            string lpUserID,
            int dwFlags,
            string lpAccessName,
            string lpBufferSize,
            string lpResult
            );

        [DllImport("Mpr.dll")]
        private static extern int WNetCancelConnection(
            string lpName,
            bool fForce
            );

        private const int RESOURCETYPE_DISK = 0x00000001;
        private const int CREATE_NEW_CONSOLE = 16;
        private const int SW_SHOW = 5;

        [StructLayout(LayoutKind.Sequential)]
        private class NETRESOURCE
        {
            public int dwScope = 0;
            public int dwType = 0;
            public int dwDisplayType = 0;
            public int dwUsage = 0;
            public string lpLocalName = "";
            public string lpRemoteName = "";
            public string lpComment = "";
            public string lpProvider = "";
        }
    }
}