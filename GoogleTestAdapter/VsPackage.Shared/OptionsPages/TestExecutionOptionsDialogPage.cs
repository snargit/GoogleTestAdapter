// This file has been modified by Microsoft on 6/2017.

using GoogleTestAdapter.Settings;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.ProcessExecution;
using Microsoft.VisualStudio.Shell;

// ReSharper disable LocalizableElement

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    [ComVisible(true)]
    // the page's GUID would otherwise depend on the assembly's version; the Unified Settings manifest refers to it
    [Guid("837bf16b-f9b8-4c9d-9be9-b700f7864fb7")]
    public class TestExecutionOptionsDialogPage : NotifyingDialogPage
    {
        #region Parallelization

        [Category(SettingsWrapper.CategoryParallelizationName)]
        [DisplayName(SettingsWrapper.OptionEnableParallelTestExecution)]
        [Description(SettingsWrapper.OptionEnableParallelTestExecutionDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.parallelization.enableParallelTestExecution")]
        public bool EnableParallelTestExecution
        {
            get => _enableParallelTestExecution;
            set => SetAndNotify(ref _enableParallelTestExecution, value);
        }
        private bool _enableParallelTestExecution = SettingsWrapper.OptionEnableParallelTestExecutionDefaultValue;

        [Category(SettingsWrapper.CategoryParallelizationName)]
        [DisplayName(SettingsWrapper.OptionMaxNrOfThreads)]
        [Description(SettingsWrapper.OptionMaxNrOfThreadsDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.parallelization.maxNrOfThreads", Minimum = 0)]
        public int MaxNrOfThreads
        {
            get => _maxNrOfThreads;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(MaxNrOfThreads), value, "Expected a number greater than or equal to 0.");
                SetAndNotify(ref _maxNrOfThreads, value);
            }
        }
        private int _maxNrOfThreads = SettingsWrapper.OptionMaxNrOfThreadsDefaultValue;

        #endregion

        #region Run configuration

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionAdditionalPdbs)]
        [Description(SettingsWrapper.OptionAdditionalPdbsDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.additionalPdbs")]
        public string AdditionalPdbs
        {
            get => _additionalPdbs;
            set
            {
                var patterns = Utils.SplitAdditionalPdbs(value ?? "");
                var errorMessages = new List<string>();
                foreach (string pattern in patterns)
                {
                    if (!Utils.ValidatePattern(pattern, out string errorMessage))
                    {
                        errorMessages.Add(errorMessage);
                    }
                }
                if (errorMessages.Any())
                {
                    throw new ArgumentException(string.Join(Environment.NewLine, errorMessages), nameof(AdditionalPdbs));
                }

                SetAndNotify(ref _additionalPdbs, value);
            }
        }
        private string _additionalPdbs = SettingsWrapper.OptionAdditionalPdbsDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionWorkingDir)]
        [Description(SettingsWrapper.OptionWorkingDirDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.workingDir")]
        public string WorkingDir
        {
            get => _workingDirectory;
            set => SetAndNotify(ref _workingDirectory, value);
        }
        private string _workingDirectory = SettingsWrapper.OptionWorkingDirDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionPathExtension)]
        [Description(SettingsWrapper.OptionPathExtensionDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.pathExtension")]
        public string PathExtension
        {
            get => _pathExtension;
            set => SetAndNotify(ref _pathExtension, value);
        }
        private string _pathExtension = SettingsWrapper.OptionPathExtensionDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionEnvironmentVariables)]
        [Description(SettingsWrapper.OptionEnvironmentVariablesDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.environmentVariables")]
        public string EnvironmentVariables
        {
            get => _environmentVariables;
            set
            {
                Utils.ValidateEnvironmentVariables(value);
                SetAndNotify(ref _environmentVariables, value);
            }
        }
        private string _environmentVariables = SettingsWrapper.OptionEnvironmentVariablesDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionUseCTestTestProperties)]
        [Description(SettingsWrapper.OptionUseCTestTestPropertiesDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.useCTestTestProperties")]
        public bool UseCTestTestProperties
        {
            get => _useCTestTestProperties;
            set => SetAndNotify(ref _useCTestTestProperties, value);
        }
        private bool _useCTestTestProperties = SettingsWrapper.OptionUseCTestTestPropertiesDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionTestTimeoutInSeconds)]
        [Description(SettingsWrapper.OptionTestTimeoutInSecondsDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.testTimeoutInSeconds", Minimum = 0)]
        public int TestTimeoutInSeconds
        {
            get => _testTimeoutInSeconds;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(TestTimeoutInSeconds), value, "Expected a number greater than or equal to 0.");
                SetAndNotify(ref _testTimeoutInSeconds, value);
            }
        }
        private int _testTimeoutInSeconds = SettingsWrapper.OptionTestTimeoutInSecondsDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionBenchmarkDryRun)]
        [Description(SettingsWrapper.OptionBenchmarkDryRunDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.benchmarkDryRun")]
        public bool BenchmarkDryRun
        {
            get => _benchmarkDryRun;
            set => SetAndNotify(ref _benchmarkDryRun, value);
        }
        private bool _benchmarkDryRun = SettingsWrapper.OptionBenchmarkDryRunDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionSourcePathMapping)]
        [Description(SettingsWrapper.OptionSourcePathMappingDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.sourcePathMapping")]
        public string SourcePathMapping
        {
            get => _sourcePathMapping;
            set
            {
                SourcePathMapper.Parse(value);
                SetAndNotify(ref _sourcePathMapping, value);
            }
        }
        private string _sourcePathMapping = SettingsWrapper.OptionSourcePathMappingDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionMapOnlyMissingSourcePaths)]
        [Description(SettingsWrapper.OptionMapOnlyMissingSourcePathsDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.mapOnlyMissingSourcePaths")]
        public bool MapOnlyMissingSourcePaths
        {
            get => _mapOnlyMissingSourcePaths;
            set => SetAndNotify(ref _mapOnlyMissingSourcePaths, value);
        }
        private bool _mapOnlyMissingSourcePaths = SettingsWrapper.OptionMapOnlyMissingSourcePathsDefaultValue;

        [Category(SettingsWrapper.CategoryRunConfigurationName)]
        [DisplayName(SettingsWrapper.OptionAdditionalTestExecutionParams)]
        [Description(SettingsWrapper.OptionAdditionalTestExecutionParamsDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.runConfiguration.additionalTestExecutionParams")]
        public string AdditionalTestExecutionParams
        {
            get => _additionalTestExecutionParams;
            set => SetAndNotify(ref _additionalTestExecutionParams, value);
        }
        private string _additionalTestExecutionParams = SettingsWrapper.OptionAdditionalTestExecutionParamsDefaultValue;

        #endregion

        #region Setup and teardown

        [Category(SettingsWrapper.CategorySetupAndTeardownName)]
        [DisplayName(SettingsWrapper.OptionBatchForTestSetup)]
        [Description(SettingsWrapper.OptionBatchForTestSetupDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.setupAndTeardown.batchForTestSetup")]
        public string BatchForTestSetup
        {
            get => _batchForTestSetup;
            set => SetAndNotify(ref _batchForTestSetup, value);
        }
        private string _batchForTestSetup = SettingsWrapper.OptionBatchForTestSetupDefaultValue;

        [Category(SettingsWrapper.CategorySetupAndTeardownName)]
        [DisplayName(SettingsWrapper.OptionBatchForTestTeardown)]
        [Description(SettingsWrapper.OptionBatchForTestTeardownDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.setupAndTeardown.batchForTestTeardown")]
        public string BatchForTestTeardown
        {
            get => _batchForTestTeardown;
            set => SetAndNotify(ref _batchForTestTeardown, value);
        }
        private string _batchForTestTeardown = SettingsWrapper.OptionBatchForTestTeardownDefaultValue;

        #endregion

        #region Misc

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionKillProcessesOnCancel)]
        [Description(SettingsWrapper.OptionKillProcessesOnCancelDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.misc.killProcessesOnCancel")]
        public bool KillProcessesOnCancel
        {
            get => _killProcessesOnCancel;
            set => SetAndNotify(ref _killProcessesOnCancel, value);
        }
        private bool _killProcessesOnCancel = SettingsWrapper.OptionKillProcessesOnCancelDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionDebuggerKind)]
        [Description(SettingsWrapper.OptionDebuggerKindDescription)]
        [PropertyPageTypeConverter(typeof(DebuggerKindConverter))]
        [UnifiedSetting("googleTestAdapter.testExecution.misc.debuggerKind")]
        public DebuggerKind DebuggerKind
        {
            get => _debuggerKind;
            set => SetAndNotify(ref _debuggerKind, value);
        }
        private DebuggerKind _debuggerKind = SettingsWrapper.OptionDebuggerKindDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionExitCodeTestCase)]
        [Description(SettingsWrapper.OptionExitCodeTestCaseDescription)]
        [UnifiedSetting("googleTestAdapter.testExecution.misc.exitCodeTestCase")]
        public string ExitCodeTestCase
        {
            get => _exitCodeTestCase;
            set => SetAndNotify(ref _exitCodeTestCase, value);
        }
        private string _exitCodeTestCase = SettingsWrapper.OptionExitCodeTestCaseDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionMissingTestsReportMode)]
        [Description(SettingsWrapper.OptionMissingTestsReportModeDescription)]
        [PropertyPageTypeConverter(typeof(MissingTestsReportModeConverter))]
        [UnifiedSetting("googleTestAdapter.testExecution.misc.missingTestsReportMode")]
        public MissingTestsReportMode MissingTestsReportMode
        {
            get => _missingTestsReportMode;
            set => SetAndNotify(ref _missingTestsReportMode, value);
        }
        private MissingTestsReportMode _missingTestsReportMode = SettingsWrapper.OptionMissingTestsReportModeDefaultValue;

        #endregion

    }

}