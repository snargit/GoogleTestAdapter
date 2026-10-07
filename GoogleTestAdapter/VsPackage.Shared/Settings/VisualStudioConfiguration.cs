using System;
using EnvDTE;

namespace GoogleTestAdapter.TestAdapter.Settings
{
    /// <summary>
    /// The solution directory and the active configuration of Visual Studio, which provide the values of the
    /// placeholders $(SolutionDir), $(PlatformName), and $(ConfigurationName).
    /// </summary>
    public class VisualStudioConfiguration
    {
        public string SolutionDir { get; set; }
        public string PlatformName { get; set; }
        public string ConfigurationName { get; set; }

        public static VisualStudioConfiguration FromDte(DTE dte, Action<string> logError)
        {
            var configuration = new VisualStudioConfiguration();
            if (dte == null)
                return configuration;

            try
            {
                try
                {
                    configuration.SolutionDir = SolutionPaths.GetSolutionDir(dte.Solution.FullName);
                }
                catch (Exception e)
                {
                    logError($"Exception caught while receiving solution dir from VS instance. dte.Solution.FullName: {dte.Solution.FullName}. Exception:{Environment.NewLine}{e}");
                }

                if (dte.Solution.Projects.Count > 0)
                {
                    var configurationManager = dte.Solution.Projects.Item(1).ConfigurationManager;
                    var activeConfiguration = configurationManager?.ActiveConfiguration;

                    configuration.PlatformName = activeConfiguration?.PlatformName;
                    configuration.ConfigurationName = activeConfiguration?.ConfigurationName;
                }
            }
            catch (Exception e)
            {
                logError($"Exception while receiving configuration info from Visual Studio.{Environment.NewLine}{e}");
            }

            return configuration;
        }
    }
}
