namespace RemoteDebug
{
    using Community.VisualStudio.Toolkit;
    using Microsoft.VisualStudio.Shell;
    using RemoteDebug.ToolWindows;
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Task = System.Threading.Tasks.Task;

    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideToolWindow(typeof(ToolWindow.Pane), Style = VsDockStyle.AlwaysFloat, Window = WindowGuids.Properties)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.DevUtilsString)]
    public sealed class Package : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();
            this.RegisterToolWindows();
        }
    }
}