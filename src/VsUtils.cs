namespace RemoteDebug
{
    using Community.VisualStudio.Toolkit;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    internal static class VsUtils
    {
        public static async Task<TextWriter> GetOutputWindowsWriterAsync(string name)
        {
            var outputWindowPane = await VS.Windows.CreateOutputWindowPaneAsync(name, true);
            return await outputWindowPane.CreateOutputPaneTextWriterAsync();
        }

        public static async Task<string> GetSolutionFolderAsync()
        {
            var solution = await VS.Solutions.GetCurrentSolutionAsync();
            return Path.GetDirectoryName(solution.FullPath);
        }

        public static async Task<IEnumerable<string>> GetAllExeProjectsAsync()
        {
            var allProjects = await VS.Solutions.GetAllProjectsAsync(ProjectStateFilter.Loaded);
            var exeProjectNames = new List<string>();

            foreach (var project in allProjects)
            {
                var outputType = await project.GetAttributeAsync("OutputType");
                if (outputType != null
                    && outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeProjectNames.Add(project.Name.Split('.').Last());
                }
            }

            return exeProjectNames;
        }

        public static async Task<bool> BuildSolutionAsync()
        {
            return await VS.Build.BuildSolutionAsync();
        }
    }
}
