namespace RemoteDebug.Commands
{
    using Community.VisualStudio.Toolkit;
    using Microsoft.VisualStudio.Shell;
    using RemoteDebug.ToolWindows;
    using System.Threading.Tasks;

    [Command(PackageIds.ShowToolWindowCommand)]
    internal sealed class ToolCommand : BaseCommand<ToolCommand>
    {
        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            OutputWindowLogger.Info("Showing tool Window");
            return ToolWindow.ShowAsync();
        }
    }
}
