namespace RemoteDebug.ToolWindows
{
    using Community.VisualStudio.Toolkit;
    using Microsoft.VisualStudio.Imaging;
    using Microsoft.VisualStudio.Shell;
    using System;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;

    public class ToolWindow : BaseToolWindow<ToolWindow>
    {
        public override string GetTitle(int toolWindowId) => "Tool Window";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            return Task.FromResult<FrameworkElement>(new ToolWindowControl());
        }

        [Guid("f0c09446-01e1-4769-b80f-5c621eca01d8")]
        internal class Pane : ToolWindowPane
        {
            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.ToolWindow;
            }
        }
    }
}