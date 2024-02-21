namespace RemoteDebug.ToolWindows
{
    using Microsoft.VisualStudio.Shell;
    using RemoteDebug.WinRM;
    using System;
    using System.Net.NetworkInformation;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;

    public partial class ToolWindowControl : UserControl
    {
        private WinRemoteDebug WinRemoteDebug;

        public ToolWindowControl()
        {
            InitializeComponent();
            this.PrerequisiteCommands.Text = WinRemoteDebug.ShowPreRequisiteCommands();
            this.TabControl.SelectionChanged += this.TabChanged;
            this.HostName.TextChanged += this.RemoteMachineDetailsChanged;
            this.UserName.TextChanged += this.RemoteMachineDetailsChanged;
            this.Password.PasswordChanged += this.RemoteMachineDetailsChanged;
            this.RemoteSetupHostName.TextChanged += this.SetupOptionsChanged;
            this.SystemContext.Checked += this.SetupOptionsChanged;
            this.SystemContext.Unchecked += this.SetupOptionsChanged;
            this.RemoteSetupPort.TextChanged += this.SetupOptionsChanged;
        }

        private async void TabChanged(object sender, RoutedEventArgs e)
        {
            if (this.PrerequisitesTab.IsSelected)
            {
                this.RemoteHostTab.IsEnabled = false;
                this.SetupTab.IsEnabled = false;
                this.DeployTab.IsEnabled = false;
                OutputWindowLogger.Info($"Moved to {nameof(this.PrerequisitesTab)}");
            }
            else if (this.RemoteHostTab.IsSelected)
            {
                this.SetupTab.IsEnabled = false;
                this.DeployTab.IsEnabled = false;
                this.MoveToSetup.IsEnabled = false;
                OutputWindowLogger.Info($"Moved to {nameof(this.RemoteHostTab)}");
                this.ShowRemoteHostError(string.Empty);
            }
            else if (this.SetupTab.IsSelected)
            {
                this.DeployTab.IsEnabled = false;
                this.MoveToDeploy.IsEnabled = false;
                OutputWindowLogger.Info($"Moved to {nameof(this.SetupTab)}");
                this.Setup.Focus();
                this.ShowSetupError(string.Empty);
            }
            else if (this.DeployTab.IsSelected)
            {
                OutputWindowLogger.Info($"Moved to {nameof(this.DeployTab)}");
                var content = this.AttachLabel.Content.ToString();
                this.AttachLabel.Content = string.Format(content, $"{this.WinRemoteDebug.remoteHost.ComputerName}:{this.RemoteSetupPort.Text}");
                this.ShowDeployError(string.Empty);
            }
        }

        private void RemoteMachineDetailsChanged(object sender, EventArgs e)
        {
            this.ShowRemoteHostError(string.Empty);
        }

        private void SetupOptionsChanged(object sender, EventArgs e)
        {
            this.ShowSetupError(string.Empty);
        }

        private void DeployOptionsChanged(object sender, EventArgs e)
        {
            this.ShowDeployError(string.Empty);
        }

        private void MoveToRemoteHost_Click(object sender, RoutedEventArgs e)
        {
            this.RemoteHostTab.IsEnabled = true;
            this.PrerequisitesTab.IsSelected = false;
            this.RemoteHostTab.IsSelected = true;
        }

        private void MoveToSetup_Click(object sender, RoutedEventArgs e)
        {
            this.SetupTab.IsEnabled = true;
            this.RemoteHostTab.IsSelected = false;
            this.SetupTab.IsSelected = true;
        }

        private void MoveToDeploy_Click(object sender, RoutedEventArgs e)
        {
            this.DeployTab.IsEnabled = true;
            this.SetupTab.IsSelected = false;
            this.DeployTab.IsSelected = true;
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            this.ShowRemoteHostProgress(string.Empty);

            var hostname = this.HostName.Text;
            var username = this.UserName.Text;
            var password = this.Password.Password;

            if (this.SetRemoteHostError(() => string.IsNullOrWhiteSpace(hostname), "Remote Machine's Hostname cannot be empty")
                || this.SetRemoteHostError(() => string.IsNullOrWhiteSpace(username), "Remote Machine's Admin Username cannot be empty")
                || this.SetRemoteHostError(() => string.IsNullOrWhiteSpace(password), "Remote Machine's Admin password cannot be empty")
                || this.SetRemoteHostError(() => Uri.CheckHostName(hostname) == UriHostNameType.Unknown, "Hostname not valid"))
            {
                return;
            }

            await this.ConnectionCheckAsync(hostname, username, password);
        }

        private void Setup_Click(object sender, RoutedEventArgs e)
        {
            this.ShowSetupProgress(string.Empty);

            if (!int.TryParse(this.RemoteSetupPort.Text, out int port))
            {
                this.ShowSetupError("Invalid Port Number");
                return;
            }

            var systemContext = this.SystemContext.IsChecked.HasValue && this.SystemContext.IsChecked.Value;

            this.WinRemoteDebug.Setup(
                port,
                systemContext,
                async str =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowSetupProgress(str);
                },
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowSetupSuccess();
                },
                async err =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowSetupError(err);
                });
        }

        private void Deploy_Click(object sender, RoutedEventArgs e)
        {
            this.ShowDeployProgress(string.Empty);

            this.WinRemoteDebug.Deploy(
                async str =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowDeployProgress(str);
                },
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowDeploySuccess();
                },
                async err =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowDeployError(err);
                });
        }

        private async Task ConnectionCheckAsync(string hostname, string username, string password)
        {
            OutputWindowLogger.Info($"Pinging remote host {hostname}");
            this.ShowRemoteHostProgress("Pinging remote host ...");

            if (await this.PingHostAsync(hostname))
            {
                await this.WinRmConnectCheckAsync(hostname, username, password);
            }
            else
            {
                OutputWindowLogger.Err($"Hostname {hostname} not reachable");
                this.ShowRemoteHostError("Hostname not reachable");
            }
        }

        private async Task WinRmConnectCheckAsync(string hostname, string username, string password)
        {
            OutputWindowLogger.Info($"attempting WinRM connection to remote host {hostname}");
            this.ShowRemoteHostProgress("Connecting to remote host ...");

            this.WinRemoteDebug = new WinRemoteDebug(hostname, username, password);

            await this.WinRemoteDebug.remoteHost.CheckConnectionAsync(
                async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.RemoteSetupHostName.Text = hostname;
                    this.ShowRemoteHostSuccess();
                },
                async err =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    this.ShowRemoteHostError(err);
                });
        }

        private async Task<bool> PingHostAsync(string host)
        {
            var attempts = 4;
            var ping = new Ping();

            while (attempts-- > 0)
            {
                try
                {
                    if ((await ping.SendPingAsync(
                            host,
                            10,
                            Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                            new PingOptions { DontFragment = true }))?.Status == IPStatus.Success)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }

            return false;
        }

        private bool SetRemoteHostError(Func<bool> errorCondition, string errorText)
        {
            if (errorCondition())
            {
                this.ShowRemoteHostError(errorText);
                return true;
            }

            return false;
        }

        private void ShowRemoteHostProgress(string text)
        {
            this.RemoteError.Text = string.Empty;
            this.RemoteInfo.Text = text;
            this.TestConnection.IsEnabled = false;
            this.MoveToSetup.IsEnabled = false;
            this.HostName.IsEnabled = false;
            this.UserName.IsEnabled = false;
            this.Password.IsEnabled = false;
        }

        private void ShowRemoteHostError(string errorText)
        {
            this.RemoteInfo.Text = string.Empty;
            this.RemoteError.Text = errorText;
            this.TestConnection.IsEnabled = true;
            this.MoveToSetup.IsEnabled = false;
            this.HostName.IsEnabled = true;
            this.UserName.IsEnabled = true;
            this.Password.IsEnabled = true;
        }

        private void ShowRemoteHostSuccess()
        {
            this.RemoteError.Text = string.Empty;
            this.RemoteInfo.Text = "Connected to remote host !";
            this.TestConnection.IsEnabled = true;
            this.MoveToSetup.IsEnabled = true;
            this.HostName.IsEnabled = true;
            this.UserName.IsEnabled = true;
            this.Password.IsEnabled = true;
        }

        private void ShowSetupProgress(string text)
        {
            this.SetupError.Text = string.Empty;
            this.SetupInfo.Text = text;
            this.MoveToDeploy.IsEnabled = false;
            this.Setup.IsEnabled = false;
            this.RemoteSetupPort.IsEnabled = false;
            this.SystemContext.IsEnabled = false;
        }

        private void ShowSetupError(string errorText)
        {
            this.SetupError.Text = errorText;
            this.SetupInfo.Text = string.Empty;
            this.MoveToDeploy.IsEnabled = false;
            this.Setup.IsEnabled = true;
            this.RemoteSetupPort.IsEnabled = true;
            this.SystemContext.IsEnabled = true;
        }

        private void ShowSetupSuccess()
        {
            this.SetupError.Text = string.Empty;
            this.MoveToDeploy.IsEnabled = true;
            this.Setup.IsEnabled = true;
            this.RemoteSetupPort.IsEnabled = true;
            this.SystemContext.IsEnabled = true;
        }

        private void ShowDeployProgress(string text)
        {
            this.DeployError.Text = string.Empty;
            this.DeployInfo.Text = text;
            this.Deploy.IsEnabled = false;
        }

        private void ShowDeployError(string errorText)
        {
            this.DeployError.Text = errorText;
            this.DeployInfo.Text = string.Empty;
            this.Deploy.IsEnabled = true;
        }

        private void ShowDeploySuccess()
        {
            this.DeployError.Text = string.Empty;
            this.Deploy.IsEnabled = true;
        }
    }
}