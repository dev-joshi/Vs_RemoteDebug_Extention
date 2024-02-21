namespace RemoteDebug
{
    using RemoteDebug.WinRM;
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    internal class WinRemoteDebug
    {
        public readonly RemoteHost remoteHost;

        private Func<string, Task> setupOnNext;
        private Func<string, Task> setupOnError;
        private Func<Task> setupOnComplete;

        private Func<string, Task> deployOnNext;
        private Func<string, Task> deployOnError;
        private Func<Task> deployOnComplete;

        private string remoteDirectoryPath;
        private string sharedDirectoryPath;
        private int port;
        private bool systemContext;

        private static string vsRemoteAppName = "Microsoft Visual Studio 2022 Remote Debugger";
        private static Version vsRemoteAppVersion = Version.Parse("17.0.32804");
        private static string installerName = "VS_RemoteTools.exe";
        private static string remoteToolExe64FullName = @"C:\Program Files\Microsoft Visual Studio 17.0\Common7\IDE\Remote Debugger\x64\msvsmon.exe";
        private static string remoteToolExe86FullName = remoteToolExe64FullName.Replace("x64", "x86");
        private static string remoteToolExeName = "msvsmon.exe";
        private static string remoteTool64FirewallRuleName = @$"Visual Studio Remote Debugger ({remoteToolExe64FullName})";
        private static string remoteTool86FirewallRuleName = @$"Visual Studio Remote Debugger ({remoteToolExe86FullName})";

        public WinRemoteDebug(
            string hostname,
            string username,
            string password)
        {
            var userNameSplit = username.Split('\\');
            var domain = userNameSplit.Length > 1 ? userNameSplit.First() : string.Empty;
            hostname = hostname.Trim();
            username = username.Trim().Replace($"{domain}\\", string.Empty);
            this.remoteHost = new RemoteHost(hostname, domain, username, password);
        }

        public static string ShowPreRequisiteCommands()
        {
            return
                // Enables WinRM service
                "winrm quickconfig -quiet -force"
                + "\n"
                // allows WinRM to run on unencrypted channel
                + "winrm set winrm/config/client @{AllowUnencrypted = \"true\"}"
                + "\n"
                // allows incoming ping requests
                + "netsh advfirewall firewall set rule name=\"File and Printer Sharing (Echo Request - ICMPv4-In)\" new enable=yes"
                + "\n";
        }

        public void Setup(int port, bool systemContext, Func<string, Task> onNext, Func<Task> onComplete, Func<string, Task> onError)
        {
            this.setupOnNext = onNext;
            this.setupOnError = onError;
            this.setupOnComplete = onComplete;
            this.port = port;
            this.systemContext = systemContext;
            _ = Task.Run(this.RunSetupAsync);
        }

        public void Deploy(Func<string, Task> onNext, Func<Task> onComplete, Func<string, Task> onError)
        {
            this.deployOnNext = onNext;
            this.deployOnError = onError;
            this.deployOnComplete = onComplete;
            _ = Task.Run(this.RunDeployAsync);
        }

        private async Task RunSetupAsync()
        {
            try
            {
                await this.setupOnNext("Starting Setup for Remote Debugging");

                this.remoteDirectoryPath = await VsUtils.GetSolutionFolderAsync();
                if (!this.remoteHost.RunCommand($"cmd.exe /c mkdir \"{this.remoteDirectoryPath}\""))
                {
                    await this.setupOnError("Running command on remote host failed");
                    return;
                }

                await this.setupOnNext("Ran command to Create Directory on remote host");
                if (!this.remoteHost.CreateSharedDirectory(this.remoteDirectoryPath))
                {
                    await this.setupOnError("Remote Shared Directory creation failed");
                    return;
                }

                await this.setupOnNext("Created Shared Directory on remote host");
                this.sharedDirectoryPath = @$"\\{remoteHost.Hostname}\{this.remoteDirectoryPath.Split('\\').Last()}";
                string resources = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources");
                if (!this.remoteHost.CopyFiles(this.sharedDirectoryPath, resources))
                {
                    await this.setupOnError("Failed to copy files");
                    return;
                }

                await this.setupOnNext("Checking if Remote Debugger is installed");
                if (!this.remoteHost.FindInstalledApp(vsRemoteAppName, vsRemoteAppVersion))
                {
                    if (!this.remoteHost.RunCommand($"cmd.exe /c \"{Path.Combine(this.remoteDirectoryPath, "Resources", installerName)}\" -quiet"))
                    {
                        await this.setupOnError("Failed to run command to install Remote Debugger");
                        return;
                    }

                    await this.setupOnNext("waiting for Remote Debugger install");
                    int attempt = 5;
                    while (attempt-- > 0)
                    {
                        OutputWindowLogger.Info("Waiting for VS Remote Tools install, next check in 30 secs");
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        if (this.remoteHost.FindInstalledApp(vsRemoteAppName, vsRemoteAppVersion))
                        {
                            break;
                        }
                        else if (attempt <= 0)
                        {
                            await this.setupOnError("Failed to install Remote Debugger");
                            return;
                        }
                    }
                }

                await this.setupOnNext("Killing all running vs remote tools");
                if (!this.remoteHost.RunCommand($"cmd.exe /c Taskkill /F /IM {remoteToolExeName}"))
                {
                    await this.setupOnError("Failed to cleanup running remote tools");
                    return;
                }

                await this.setupOnNext("Exempting VS Remote Tools from firewall");
                //this.remoteHost.RunCommand($"\"{remoteToolExe64FullName}\" /prepcomputer /quiet")
                if (!this.remoteHost.RunCommand($"netsh advfirewall firewall add rule name=\"{remoteTool64FirewallRuleName}\" dir=in action=allow program=\"{remoteToolExe64FullName}\" profile=any")
                    || !this.remoteHost.RunCommand($"netsh advfirewall firewall add rule name=\"{remoteTool86FirewallRuleName}\" dir=in action=allow program=\"{remoteToolExe86FullName}\" profile=any"))
                {
                    await this.setupOnError("Failed to exempt VS Remote Tools from firewall");
                    return;
                }

                var commandLineStart = $"\"{remoteToolExe64FullName}\" /nosecuritywarn /timeout 90000 /port {port} /noauth /anyuser /silent";
                var psexecParams = "-nobanner -accepteula " + (systemContext ? "-sdi" : "-di");
                var psexecCommand = $"\"{Path.Combine(this.remoteDirectoryPath, "Resources", "PsExec.exe")}\" {psexecParams} {commandLineStart}";

                await this.setupOnNext("starting remote tools");
                if (!this.remoteHost.RunCommand(psexecCommand))
                {
                    await this.setupOnError("Failed to start remote tools");
                    return;
                }

                OutputWindowLogger.Info($"Setup complete for Remote Debugging on {this.remoteHost.ComputerName}:{port}");
                await this.setupOnNext($"{this.remoteHost.ComputerName}:{port} is set-up !");
                await this.setupOnComplete();
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Setup Failed");
                await this.setupOnError("Setup Failed");
            }
        }

        private async Task RunDeployAsync()
        {
            try
            {
                await this.deployOnNext("Building solution");
                if (!await VsUtils.BuildSolutionAsync())
                {
                    await this.deployOnError("Failed to build solution");
                    return;
                }

                await this.deployOnNext("copying binaries");
                var solutionDir = await VsUtils.GetSolutionFolderAsync();
                var executables = Directory.GetFiles(solutionDir, "*.exe", SearchOption.AllDirectories)
                    .Where(x => (x.Contains(@"\Bin\") || x.Contains(@"\bin\")) && !x.Contains("Test") && !x.Contains("Benchmarks"));

                if (!this.remoteHost.CopyFoldersToSamePath(this.sharedDirectoryPath, executables.Select(x => Path.GetDirectoryName(x)).Distinct()))
                {
                    await this.setupOnError("Failed to copy binaries");
                    return;
                }

                OutputWindowLogger.Info("deploy completed");
                await this.deployOnNext($"binaries sent {this.remoteHost.ComputerName}:{port} on {DateTime.Now:hh:mm:ss} !");
                await this.deployOnComplete();
            }
            catch (Exception ex)
            {
                OutputWindowLogger.Err(ex, "Deploy Failed");
                await this.setupOnError("Deploy Failed");
            }
        }
    }
}