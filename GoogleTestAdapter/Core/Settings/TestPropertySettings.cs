using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GoogleTestAdapter.Settings
{
    /// <summary>
    /// Working directory and environment of a test as configured with CMake (i.e., the WORKING_DIRECTORY and
    /// ENVIRONMENT test properties), provided by Visual Studio for tests of CMake projects.
    /// </summary>
    public class TestPropertySettings : IEquatable<TestPropertySettings>
    {
        public string WorkingDirectory { get; }
        public IDictionary<string, string> Environment { get; }

        public TestPropertySettings(string workingDirectory, IDictionary<string, string> environment)
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
            Environment = new Dictionary<string, string>(environment ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }

        public bool Equals(TestPropertySettings other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;

            return string.Equals(WorkingDirectory, other.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
                   && Environment.Count == other.Environment.Count
                   && Environment.All(kvp => other.Environment.TryGetValue(kvp.Key, out string value) && value == kvp.Value);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TestPropertySettings);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(WorkingDirectory ?? "") ^ Environment.Count;
        }

        public override string ToString()
        {
            string environment = string.Join(", ", Environment.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            return $"working directory: '{WorkingDirectory}', environment: {{{environment}}}";
        }
    }

    public class TestPropertySettingsContainer
    {
        public class TestProperties
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public string WorkingDirectory { get; set; }
            public IDictionary<string, string> Environment { get; set; }
        }

        private readonly IDictionary<string, IDictionary<string, TestPropertySettings>> _settingsByExecutable =
            new Dictionary<string, IDictionary<string, TestPropertySettings>>(StringComparer.OrdinalIgnoreCase);
        private readonly IDictionary<string, TestPropertySettings> _commonSettingsByExecutable =
            new Dictionary<string, TestPropertySettings>(StringComparer.OrdinalIgnoreCase);

        public TestPropertySettingsContainer(IEnumerable<TestProperties> tests)
        {
            foreach (var test in tests)
            {
                string executable = GetFullPath(test.Command);
                if (executable == null || string.IsNullOrEmpty(test.Name))
                    continue;

                if (!_settingsByExecutable.TryGetValue(executable, out var settingsOfTests))
                {
                    settingsOfTests = new Dictionary<string, TestPropertySettings>();
                    _settingsByExecutable.Add(executable, settingsOfTests);
                }
                // CMake might provide the same test more than once - first one wins
                if (!settingsOfTests.ContainsKey(test.Name))
                    settingsOfTests.Add(test.Name, new TestPropertySettings(test.WorkingDirectory, test.Environment));
            }

            foreach (var executableAndSettings in _settingsByExecutable)
            {
                var distinctSettings = executableAndSettings.Value.Values.Distinct().ToList();
                if (distinctSettings.Count == 1)
                    _commonSettingsByExecutable.Add(executableAndSettings.Key, distinctSettings[0]);
            }
        }

        public bool IsEmpty => _settingsByExecutable.Count == 0;

        /// <summary>
        /// Settings shared by all tests of the executable, null if there are none or if they differ between tests.
        /// </summary>
        public TestPropertySettings GetSettingsForExecutable(string executable)
        {
            string fullPath = GetFullPath(executable);
            return fullPath != null && _commonSettingsByExecutable.TryGetValue(fullPath, out var settings)
                ? settings
                : null;
        }

        public TestPropertySettings GetSettingsForTest(string executable, string fullyQualifiedName)
        {
            string fullPath = GetFullPath(executable);
            if (fullPath == null || !_settingsByExecutable.TryGetValue(fullPath, out var settingsOfTests))
                return null;

            if (settingsOfTests.TryGetValue(fullyQualifiedName, out var settings))
                return settings;

            // gtest_add_tests() names parameterized tests like "*/Suite.Test/*"
            int firstSlash = fullyQualifiedName.IndexOf('/');
            int lastSlash = fullyQualifiedName.LastIndexOf('/');
            if (firstSlash >= 0 && lastSlash > firstSlash
                && settingsOfTests.TryGetValue($"*{fullyQualifiedName.Substring(firstSlash, lastSlash - firstSlash)}/*", out settings))
                return settings;

            // names do not need to match the ones of Google Test (e.g., gtest_discover_tests() with TEST_PREFIX,
            // or pretty names of parameterized tests), but usually all tests of an executable share their settings
            return GetSettingsForExecutable(executable);
        }

        private static string GetFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                return Path.GetFullPath(path.Trim('"'));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
