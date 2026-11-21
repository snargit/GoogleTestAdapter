using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoogleTestAdapter.TestAdapter.Settings;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace GoogleTestAdapter.VsPackage.Automation
{
    /// <summary>
    /// Uses services available since Visual Studio 2017 (no DTE, whose interop types are incompatible with the managed
    /// DTE implementation of VS 2026); the text of output panes is read in the ways Visual Studio versions provide it.
    /// Calls of other processes arrive on background threads, so the methods switch to Visual Studio's UI thread.
    /// </summary>
    public class VisualStudioEnvironment : IVisualStudioEnvironment
    {
        // DefGuidList.guidIWpfTextViewHost (Microsoft.VisualStudio.Editor), the IVsUserData key of a pane's text view
        private static readonly Guid WpfTextViewHostKey = new Guid("8C40265E-9FDB-4f54-A0FD-EBB72B7D0476");

        // the type of solution folders: in .sln files, and as the hierarchy reports it (EnvDTE's vsProjectKindSolutionItems)
        private static readonly Guid[] SolutionFolderTypes =
        {
            new Guid("2150E333-8FDC-42A3-9474-1A3956D46DE8"),
            new Guid("66A26722-8FB5-11D2-AA7E-00C04F688DDE")
        };

        private readonly IServiceProvider _serviceProvider;

        public VisualStudioEnvironment(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public string SolutionFullName =>
            OnUiThread(() => VisualStudioConfiguration.FromServiceProvider(_serviceProvider, _ => { }).SolutionFullName);

        public IList<ProjectOutput> GetProjectOutputs() => OnUiThread(GetProjectOutputsOnUiThread);

        public string GetOutputPaneText(Guid pane) => ThreadHelper.JoinableTaskFactory.Run(() => GetOutputPaneTextAsync(pane));

        // the calling thread (a thread of COM's RPC runtime) only waits for the result, so this can not deadlock
        private static T OnUiThread<T>(Func<T> function)
        {
            if (ThreadHelper.CheckAccess())
                return function();
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return function();
            });
        }

        private IList<ProjectOutput> GetProjectOutputsOnUiThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var outputs = new List<ProjectOutput>();
            var solution = _serviceProvider.GetService(typeof(SVsSolution)) as IVsSolution;
            var buildManager = _serviceProvider.GetService(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager;
            Guid ignored = Guid.Empty;
            if (solution == null || buildManager == null
                || ErrorHandler.Failed(solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref ignored, out IEnumHierarchies projects)))
                return outputs;

            var hierarchies = new IVsHierarchy[1];
            while (projects.Next(1, hierarchies, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                IVsHierarchy hierarchy = hierarchies[0];
                if (IsSolutionFolder(hierarchy))
                    continue;
                ProjectOutput output = GetProjectOutput(hierarchy, buildManager);
                // e.g. solution folders, if their type is not recognized: projects have project files
                if (string.IsNullOrEmpty(output.ProjectFile) && output.Configuration == null)
                    continue;
                outputs.Add(output);
            }
            return outputs;
        }

        private static bool IsSolutionFolder(IVsHierarchy hierarchy)
        {
            return ErrorHandler.Succeeded(hierarchy.GetGuidProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_TypeGuid, out Guid type))
                   && SolutionFolderTypes.Contains(type);
        }

        // as Test Explorer's ProjectDataImpl determines the outputs of projects which are not based on CPS (e.g. C++
        // projects): the key output of the active configuration's "Built" output group
        private static ProjectOutput GetProjectOutput(IVsHierarchy hierarchy, IVsSolutionBuildManager buildManager)
        {
            var output = new ProjectOutput();
            try
            {
                if (ErrorHandler.Succeeded(hierarchy.GetProperty((uint)VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_Name, out object name)))
                    output.Name = name as string;
                if (hierarchy is IVsProject project && ErrorHandler.Succeeded(project.GetMkDocument((uint)VSConstants.VSITEMID.Root, out string projectFile)))
                    output.ProjectFile = projectFile;

                var configurations = new IVsProjectCfg[1];
                if (ErrorHandler.Failed(buildManager.FindActiveProjectCfg(IntPtr.Zero, IntPtr.Zero, hierarchy, configurations)) || configurations[0] == null)
                {
                    output.Error = "The project has no active configuration";
                    return output;
                }
                if (ErrorHandler.Succeeded(configurations[0].get_CanonicalName(out string configuration)))
                    output.Configuration = configuration;

                if (hierarchy is IVsBuildPropertyStorage storage
                    && ErrorHandler.Succeeded(storage.GetPropertyValue("IsTestProject", null, (uint)_PersistStorageType.PST_PROJECT_FILE, out string isTestProject))
                    && bool.TryParse(isTestProject, out bool value))
                    output.IsTestProject = value;

                if (!(configurations[0] is IVsProjectCfg2 configuration2)
                    || ErrorHandler.Failed(configuration2.OpenOutputGroup("Built", out IVsOutputGroup outputGroup)) || outputGroup == null)
                {
                    output.Error = "The project's configuration has no output group 'Built'";
                    return output;
                }
                output.KeyOutput = GetKeyOutput(outputGroup);
                if (output.KeyOutput == null)
                    output.Error = "The project's output group 'Built' has no key output";
            }
            catch (Exception e)
            {
                output.Error = $"{e.GetType().Name}: {e.Message}";
            }
            return output;
        }

        private static string GetKeyOutput(IVsOutputGroup outputGroup)
        {
            if (outputGroup is IVsOutputGroup2 outputGroup2
                && ErrorHandler.Succeeded(outputGroup2.get_KeyOutputObject(out IVsOutput2 keyOutput)) && keyOutput != null)
                return GetPath(keyOutput);

            if (ErrorHandler.Failed(outputGroup.get_KeyOutput(out string keyOutputName)) || string.IsNullOrEmpty(keyOutputName))
                return null;
            var count = new uint[1];
            if (ErrorHandler.Failed(outputGroup.get_Outputs(0, null, count)) || count[0] == 0)
                return null;
            var outputs = new IVsOutput2[count[0]];
            if (ErrorHandler.Failed(outputGroup.get_Outputs(count[0], outputs, count)))
                return null;
            return outputs
                .Where(o => o != null && ErrorHandler.Succeeded(o.get_CanonicalName(out string canonicalName))
                            && string.Equals(NormalizePath(canonicalName), NormalizePath(keyOutputName), StringComparison.OrdinalIgnoreCase))
                .Select(GetPath)
                .FirstOrDefault();
        }

        private static string GetPath(IVsOutput2 output)
        {
            string path = null;
            try
            {
                if (ErrorHandler.Succeeded(output.get_Property("OUTPUTLOC", out object location)))
                    path = location as string;
            }
            catch (NotImplementedException)
            {
            }
            if (string.IsNullOrEmpty(path) && ErrorHandler.Succeeded(output.get_DeploySourceURL(out string url)) && !string.IsNullOrEmpty(url))
                path = new Uri(url).LocalPath;
            return string.IsNullOrEmpty(path) ? null : NormalizePath(path);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return path;
            }
        }

        // the Output window (StandardToolWindows.Output)
        private static readonly Guid OutputWindow = new Guid("34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3");

        private async System.Threading.Tasks.Task<string> GetOutputPaneTextAsync(Guid pane)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var outputWindow = _serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null || ErrorHandler.Failed(outputWindow.GetPane(ref pane, out IVsOutputWindowPane outputPane)) || outputPane == null)
                return null;

            string text = ReadPane(outputPane);
            if (text != null)
                return text;

            // Visual Studio creates a pane's text view (and provides its text) only when the pane is shown, so the pane
            // is shown in the Output window (without activating it), which then gets the time to create the view
            ShowPane(outputPane);
            for (int attempt = 0; attempt < 20 && text == null; attempt++)
            {
                await System.Threading.Tasks.Task.Delay(100);
                text = ReadPane(outputPane);
            }
            if (text != null)
                return text;

            throw new NotSupportedException("Visual Studio does not provide the pane's text: ask the user to show the pane in the Output window " +
                                            "(View > Output, 'Show output from'), then call again");
        }

        private void ShowPane(IVsOutputWindowPane outputPane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            outputPane.Activate();
            Guid outputWindow = OutputWindow;
            if (_serviceProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell
                && ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref outputWindow, out IVsWindowFrame frame))
                && frame != null)
                frame.ShowNoActivate();
        }

        // the text of the pane's text view (whose interfaces are used by reflection, so that GTA does not depend on the
        // editor's assemblies), or of the pane itself if it is a text buffer; null if neither is available (yet)
        private static string ReadPane(IVsOutputWindowPane outputPane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (outputPane is IVsUserData userData)
            {
                Guid key = WpfTextViewHostKey;
                if (ErrorHandler.Succeeded(userData.GetData(ref key, out object viewHost)) && viewHost != null)
                {
                    object textView = GetInterfaceProperty(viewHost, "Microsoft.VisualStudio.Text.Editor.IWpfTextViewHost", "TextView");
                    object textBuffer = GetInterfaceProperty(textView, "Microsoft.VisualStudio.Text.Editor.ITextView", "TextBuffer");
                    object snapshot = GetInterfaceProperty(textBuffer, "Microsoft.VisualStudio.Text.ITextBuffer", "CurrentSnapshot");
                    if (snapshot != null)
                    {
                        Type snapshotInterface = snapshot.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == "Microsoft.VisualStudio.Text.ITextSnapshot");
                        if (snapshotInterface?.GetMethod("GetText", Type.EmptyTypes)?.Invoke(snapshot, null) is string text)
                            return text;
                    }
                }
            }

            if (outputPane is IVsTextLines lines
                && ErrorHandler.Succeeded(lines.GetLastLineIndex(out int lastLine, out int lastIndex))
                && ErrorHandler.Succeeded(lines.GetLineText(0, 0, lastLine, lastIndex, out string allText)))
                return allText;

            return null;
        }

        private static object GetInterfaceProperty(object instance, string interfaceName, string property)
        {
            Type type = instance?.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == interfaceName);
            return type?.GetProperty(property)?.GetValue(instance);
        }
    }
}
