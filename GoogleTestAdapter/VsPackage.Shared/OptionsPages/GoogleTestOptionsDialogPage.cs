// This file has been modified by Microsoft on 6/2017.

using GoogleTestAdapter.Settings;
using System.Runtime.InteropServices;
using System;
using System.ComponentModel;
// ReSharper disable LocalizableElement

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    [ComVisible(true)]
    public class GoogleTestOptionsDialogPage : NotifyingDialogPage
    {
        #region Runtime behavior

        [Category(SettingsWrapper.CategoryRuntimeBehaviorName)]
        [DisplayName(SettingsWrapper.OptionCatchExceptions)]
        [Description(SettingsWrapper.OptionCatchExceptionsDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.runtimeBehavior.catchExceptions")]
        public bool CatchExceptions
        {
            get => _catchExceptions;
            set => SetAndNotify(ref _catchExceptions, value);
        }
        private bool _catchExceptions = SettingsWrapper.OptionCatchExceptionsDefaultValue;

        [Category(SettingsWrapper.CategoryRuntimeBehaviorName)]
        [DisplayName(SettingsWrapper.OptionBreakOnFailure)]
        [Description(SettingsWrapper.OptionBreakOnFailureDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.runtimeBehavior.breakOnFailure")]
        public bool BreakOnFailure
        {
            get => _breakOnFailure;
            set => SetAndNotify(ref _breakOnFailure, value);
        }
        private bool _breakOnFailure = SettingsWrapper.OptionBreakOnFailureDefaultValue;

        #endregion

        #region Test execution

        [Category(SettingsWrapper.CategoryTestExecutionName)]
        [DisplayName(SettingsWrapper.OptionRunDisabledTests)]
        [Description(SettingsWrapper.OptionRunDisabledTestsDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.testExecution.runDisabledTests")]
        public bool RunDisabledTests
        {
            get => _runDisabledTests;
            set => SetAndNotify(ref _runDisabledTests, value);
        }
        private bool _runDisabledTests = SettingsWrapper.OptionRunDisabledTestsDefaultValue;

        [Category(SettingsWrapper.CategoryTestExecutionName)]
        [DisplayName(SettingsWrapper.OptionNrOfTestRepetitions)]
        [Description(SettingsWrapper.OptionNrOfTestRepetitionsDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.testExecution.nrOfTestRepetitions", Minimum = -1)]
        public int NrOfTestRepetitions
        {
            get => _nrOfTestRepetitions;
            set
            {
                if (value < -1)
                    throw new ArgumentOutOfRangeException(nameof(NrOfTestRepetitions), value, "Expected a number greater than or equal to -1.");
                SetAndNotify(ref _nrOfTestRepetitions, value);
            }
        }
        private int _nrOfTestRepetitions = SettingsWrapper.OptionNrOfTestRepetitionsDefaultValue;

        [Category(SettingsWrapper.CategoryTestExecutionName)]
        [DisplayName(SettingsWrapper.OptionShuffleTests)]
        [Description(SettingsWrapper.OptionShuffleTestsDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.testExecution.shuffleTests")]
        public bool ShuffleTests
        {
            get => _shuffleTests;
            set => SetAndNotify(ref _shuffleTests, value);
        }
        private bool _shuffleTests = SettingsWrapper.OptionShuffleTestsDefaultValue;

        [Category(SettingsWrapper.CategoryTestExecutionName)]
        [DisplayName(SettingsWrapper.OptionShuffleTestsSeed)]
        [Description(SettingsWrapper.OptionShuffleTestsSeedDescription)]
        [UnifiedSetting("googleTestAdapter.googleTest.testExecution.shuffleTestsSeed", Minimum = GoogleTestConstants.ShuffleTestsSeedMinValue, Maximum = 99999)]
        public int ShuffleTestsSeed
        {
            get => _shuffleTestsSeed;
            set
            {
                GoogleTestConstants.ValidateShuffleTestsSeedValue(value);
                SetAndNotify(ref _shuffleTestsSeed, value);
            }
        }
        private int _shuffleTestsSeed = SettingsWrapper.OptionShuffleTestsSeedDefaultValue;

        #endregion
    }

}