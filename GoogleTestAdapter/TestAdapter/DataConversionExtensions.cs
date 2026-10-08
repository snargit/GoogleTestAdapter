using System;
using System.Linq;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.Model;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using TestCase = GoogleTestAdapter.Model.TestCase;
using TestOutcome = GoogleTestAdapter.Model.TestOutcome;
using TestResult = GoogleTestAdapter.Model.TestResult;
using Trait = GoogleTestAdapter.Model.Trait;
using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;
using VsTestProperty = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestProperty;
using VsTestPropertyAttributes = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestPropertyAttributes;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;
using VsTestOutcome = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestOutcome;
using VsTrait = Microsoft.VisualStudio.TestPlatform.ObjectModel.Trait;

namespace GoogleTestAdapter.TestAdapter
{

    public static class DataConversionExtensions
    {
        private static readonly VsTestProperty TestMetaDataProperty;

        // Test Explorer groups tests by project, namespace and class; without this property, it guesses namespace
        // and class from the fully qualified name (i.e., the namespace is always empty for Google Test names)
        private static readonly VsTestProperty HierarchyProperty;
        private const int HierarchyIndexNamespace = 1;

        static DataConversionExtensions()
        {
            TestMetaDataProperty = VsTestProperty.Register(TestCaseMetaDataProperty.Id, TestCaseMetaDataProperty.Label, typeof(string), typeof(VsTestCase));
            // same registration as Test Explorer and MSTest
            HierarchyProperty = VsTestProperty.Register("TestCase.Hierarchy", "Hierarchy", typeof(string[]), VsTestPropertyAttributes.Immutable, typeof(VsTestCase));
        }


        public static TestCase ToTestCase(this VsTestCase vsTestCase)
        {
            var testCase = new TestCase(vsTestCase.FullyQualifiedName, vsTestCase.Source, 
                vsTestCase.DisplayName, vsTestCase.CodeFilePath, vsTestCase.LineNumber);
            testCase.Traits.AddRange(vsTestCase.Traits.Select(ToTrait));

            var metaDataSerialization = vsTestCase.GetPropertyValue(TestMetaDataProperty);
            if (metaDataSerialization != null)
                testCase.Properties.Add(new TestCaseMetaDataProperty((string)metaDataSerialization));

            if (vsTestCase.GetPropertyValue(HierarchyProperty) is string[] hierarchy && hierarchy.Length == 4)
                testCase.Namespace = hierarchy[HierarchyIndexNamespace];

            return testCase;
        }

        public static VsTestCase ToVsTestCase(this TestCase testCase)
        {
            var vsTestCase = new VsTestCase(testCase.FullyQualifiedName, TestExecutor.ExecutorUri, testCase.Source)
            {
                DisplayName = testCase.DisplayName,
                CodeFilePath = testCase.CodeFilePath,
                LineNumber = testCase.LineNumber
            };

            vsTestCase.Traits.AddRange(testCase.Traits.Select(ToVsTrait));

            var property = testCase.Properties.OfType<TestCaseMetaDataProperty>().SingleOrDefault();
            if (property != null)
                vsTestCase.SetPropertyValue(TestMetaDataProperty, property.Serialization);

            if (testCase.Namespace != null)
                vsTestCase.SetPropertyValue(HierarchyProperty, GetHierarchy(testCase));

            return vsTestCase;
        }

        // project (filled in by VS if null), namespace, class (i.e., the test suite), test group (as derived by VS
        // from the fully qualified name if the property is not set)
        private static string[] GetHierarchy(TestCase testCase)
        {
            string fullyQualifiedName = testCase.FullyQualifiedName;
            int indexOfTestName = fullyQualifiedName.LastIndexOf('.');
            string suite = indexOfTestName > 0 ? fullyQualifiedName.Substring(0, indexOfTestName) : fullyQualifiedName;
            return new[] { null, testCase.Namespace, suite, fullyQualifiedName };
        }


        private static Trait ToTrait(this VsTrait trait)
        {
            return new Trait(trait.Name, trait.Value);
        }

        private static VsTrait ToVsTrait(this Trait trait)
        {
            return new VsTrait(trait.Name, trait.Value);
        }


        public static VsTestResult ToVsTestResult(this TestResult testResult)
        {
            var vsTestResult = new VsTestResult(ToVsTestCase(testResult.TestCase))
            {
                Outcome = testResult.Outcome.ToVsTestOutcome(),
                ComputerName = testResult.ComputerName,
                DisplayName = testResult.DisplayName,
                Duration = testResult.Duration,
                ErrorMessage = testResult.ErrorMessage,
                ErrorStackTrace = testResult.ErrorStackTrace
            };
            if (!string.IsNullOrEmpty(testResult.StandardOutput))
                vsTestResult.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, testResult.StandardOutput));
            return vsTestResult;
        }


        public static VsTestOutcome ToVsTestOutcome(this TestOutcome testOutcome)
        {
            switch (testOutcome)
            {
                case TestOutcome.Passed:
                    return VsTestOutcome.Passed;
                case TestOutcome.Failed:
                    return VsTestOutcome.Failed;
                case TestOutcome.Skipped:
                    return VsTestOutcome.Skipped;
                case TestOutcome.None:
                    return VsTestOutcome.None;
                case TestOutcome.NotFound:
                    return VsTestOutcome.NotFound;
                default:
                    throw new InvalidOperationException($"Unknown enum literal: {testOutcome}");
            }
        }

        public static Severity GetSeverity(this TestMessageLevel level)
        {
            switch (level)
            {
                case TestMessageLevel.Informational: return Severity.Info;
                case TestMessageLevel.Warning: return Severity.Warning;
                case TestMessageLevel.Error: return Severity.Error;
                default:
                    throw new InvalidOperationException($"Unknown enum literal: {level}");
            }
        }

        public static TestMessageLevel GetTestMessageLevel(this Severity severity)
        {
            switch (severity)
            {
                case Severity.Info: return TestMessageLevel.Informational;
                case Severity.Warning: return TestMessageLevel.Warning;
                case Severity.Error: return TestMessageLevel.Error;
                default:
                    throw new InvalidOperationException($"Unknown enum literal: {severity}");
            }
        }

    }

}