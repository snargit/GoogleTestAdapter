using System;
using System.Collections.Generic;

namespace GoogleTestAdapter.VsPackage.Automation
{
    /// <summary>
    /// What the automation object needs from Visual Studio beyond the run settings (an interface, so that the
    /// automation object can be tested without Visual Studio). Must be called on Visual Studio's UI thread.
    /// </summary>
    public interface IVisualStudioEnvironment
    {
        /// <summary>
        /// The full name of the solution file (or of the opened folder in Open Folder mode), or null if no solution is
        /// open
        /// </summary>
        string SolutionFullName { get; }

        /// <summary>
        /// The loaded projects of the solution (no solution folders) with the outputs of their active configurations,
        /// as Test Explorer determines them for its test containers
        /// </summary>
        IList<ProjectOutput> GetProjectOutputs();

        /// <summary>
        /// The text of an output window pane
        /// </summary>
        /// <returns>null if the pane does not exist</returns>
        /// <exception cref="NotSupportedException">if Visual Studio does not provide the pane's text</exception>
        string GetOutputPaneText(Guid pane);
    }

    public class ProjectOutput
    {
        public string Name { get; set; }
        public string ProjectFile { get; set; }

        /// <summary>
        /// "Configuration|Platform" of the project configuration which is built with the solution's active
        /// configuration, or null if there is none
        /// </summary>
        public string Configuration { get; set; }

        /// <summary>
        /// The full path of the key output (e.g. the .exe) of the active configuration, or null if there is none
        /// </summary>
        public string KeyOutput { get; set; }

        /// <summary>
        /// The value of the project's MSBuild property IsTestProject, or null if it is not set (Test Explorer ignores
        /// the outputs of projects setting it to false)
        /// </summary>
        public bool? IsTestProject { get; set; }

        /// <summary>
        /// Why the output could not be determined, or null
        /// </summary>
        public string Error { get; set; }
    }
}
