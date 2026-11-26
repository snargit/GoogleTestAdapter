// This file has been modified by Microsoft on 6/2017.

using GoogleTestAdapter.Settings;
using System.Runtime.InteropServices;
using System;
using System.ComponentModel;
using GoogleTestAdapter.Helpers;
// ReSharper disable LocalizableElement

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    [ComVisible(true)]
    // the page's GUID would otherwise depend on the assembly's version; the Unified Settings manifest refers to it
    [Guid("4d5bf0ff-3e29-4c5a-a4e4-1cff8fdb6812")]
    public class TestDiscoveryOptionsDialogPage : NotifyingDialogPage
    {
        #region Misc

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionTestDiscoveryRegex)]
        [Description(SettingsWrapper.OptionTestDiscoveryRegexDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.testDiscoveryRegex")]
        public string TestDiscoveryRegex
        {
            get => _testDiscoveryRegex;
            set
            {
                Utils.ValidateRegex(value);
                SetAndNotify(ref _testDiscoveryRegex, value);
            }
        }
        private string _testDiscoveryRegex = SettingsWrapper.OptionTestDiscoveryRegexDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionTestDiscoveryTimeoutInSeconds)]
        [Description(SettingsWrapper.OptionTestDiscoveryTimeoutInSecondsDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.testDiscoveryTimeoutInSeconds", Minimum = 0)]
        public int TestDiscoveryTimeoutInSeconds
        {
            get => _testDiscoveryTimeoutInSeconds;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(TestDiscoveryTimeoutInSeconds), value, "Expected a number greater than or equal to 0.");
                SetAndNotify(ref _testDiscoveryTimeoutInSeconds, value);
            }
        }
        private int _testDiscoveryTimeoutInSeconds = SettingsWrapper.OptionTestDiscoveryTimeoutInSecondsDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionTestNameSeparator)]
        [Description(SettingsWrapper.OptionTestNameSeparatorDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.testNameSeparator", MaxLength = 16)]
        public string TestNameSeparator
        {
            get => _testNameSeparator;
            set
            {
                if (value.Length > 16)
                    throw new ArgumentOutOfRangeException(nameof(TestNameSeparator), value, "Expected string not longer than 16 characters.");
                SetAndNotify(ref _testNameSeparator, value);
            }
        }
        private string _testNameSeparator = SettingsWrapper.OptionTestNameSeparatorDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionParseSymbolInformation)]
        [Description(SettingsWrapper.OptionParseSymbolInformationDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.parseSymbolInformation")]
        public bool ParseSymbolInformation
        {
            get => _parseSymbolInformation;
            set => SetAndNotify(ref _parseSymbolInformation, value);
        }
        private bool _parseSymbolInformation = SettingsWrapper.OptionParseSymbolInformationDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionTearDownTestSuiteTestCases)]
        [Description(SettingsWrapper.OptionTearDownTestSuiteTestCasesDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.tearDownTestSuiteTestCases")]
        public bool TearDownTestSuiteTestCases
        {
            get => _tearDownTestSuiteTestCases;
            set => SetAndNotify(ref _tearDownTestSuiteTestCases, value);
        }
        private bool _tearDownTestSuiteTestCases = SettingsWrapper.OptionTearDownTestSuiteTestCasesDefaultValue;

        [Category(SettingsWrapper.CategoryMiscName)]
        [DisplayName(SettingsWrapper.OptionAdditionalTestDiscoveryParams)]
        [Description(SettingsWrapper.OptionAdditionalTestDiscoveryParamsDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.misc.additionalTestDiscoveryParams")]
        public string AdditionalTestDiscoveryParams
        {
            get => _additionalTestDiscoveryParams;
            set => SetAndNotify(ref _additionalTestDiscoveryParams, value);
        }
        private string _additionalTestDiscoveryParams = SettingsWrapper.OptionAdditionalTestDiscoveryParamsDefaultValue;

        #endregion

        #region Traits

        [Category(SettingsWrapper.CategoryTraitsName)]
        [DisplayName(SettingsWrapper.OptionTraitsRegexesBefore)]
        [Description(SettingsWrapper.OptionTraitsDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.traits.traitsRegexesBefore")]
        public string TraitsRegexesBefore
        {
            get => _traitsRegexesBefore;
            set
            {
                Utils.ValidateTraitRegexes(value);
                SetAndNotify(ref _traitsRegexesBefore, value);
            }
        }
        private string _traitsRegexesBefore = SettingsWrapper.OptionTraitsRegexesDefaultValue;

        [Category(SettingsWrapper.CategoryTraitsName)]
        [DisplayName(SettingsWrapper.OptionTraitsRegexesAfter)]
        [Description(SettingsWrapper.OptionTraitsDescription)]
        [UnifiedSetting("googleTestAdapter.testDiscovery.traits.traitsRegexesAfter")]
        public string TraitsRegexesAfter
        {
            get => _traitsRegexesAfter;
            set
            {
                Utils.ValidateTraitRegexes(value);
                SetAndNotify(ref _traitsRegexesAfter, value);
            }
        }
        private string _traitsRegexesAfter = SettingsWrapper.OptionTraitsRegexesDefaultValue;

        #endregion

    }

}