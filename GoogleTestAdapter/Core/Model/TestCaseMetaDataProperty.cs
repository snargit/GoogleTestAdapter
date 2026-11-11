using System;
using System.Linq;

namespace GoogleTestAdapter.Model
{
    public class TestCaseMetaDataProperty : TestProperty
    {
        public static readonly string Id = $"{typeof(TestCaseMetaDataProperty).FullName}";
        public const string Label = "Test case meta data";

        private const string TearDownTestSuiteMarker = "TearDownTestSuite";
        private const string BenchmarkMarker = "Benchmark";

        /// <summary>
        /// The number of test cases in the test case's suite and executable, not counting TearDownTestSuite test cases
        /// </summary>
        public int NrOfTestCasesInSuite { get; }
        public int NrOfTestCasesInExecutable { get; }

        /// <summary>
        /// Whether the test case reports the result of the suite's TearDownTestSuite() (see option
        /// TearDownTestSuiteTestCases) rather than being a test of the executable
        /// </summary>
        public bool IsTearDownTestSuite { get; }

        /// <summary>
        /// Whether the test case is a benchmark of a Google Benchmark executable
        /// </summary>
        public bool IsBenchmark { get; }

        public TestCaseMetaDataProperty(int nrOfTestCasesInSuite, int nrOfTestCasesInExecutable, bool isTearDownTestSuite = false, bool isBenchmark = false)
            : this($"{nrOfTestCasesInSuite}:{nrOfTestCasesInExecutable}"
                   + (isTearDownTestSuite ? $":{TearDownTestSuiteMarker}" : "")
                   + (isBenchmark ? $":{BenchmarkMarker}" : ""))
        {
        }

        public TestCaseMetaDataProperty(string serialization) : base(serialization)
        {
            string[] values = serialization.Split(':');
            if (values.Length < 2 || values.Length > 3 || values.Skip(2).Any(v => v != TearDownTestSuiteMarker && v != BenchmarkMarker))
                throw new ArgumentException(serialization, nameof(serialization));
            int[] numbers = values.Take(2).Select(int.Parse).ToArray();
            NrOfTestCasesInSuite = numbers[0];
            NrOfTestCasesInExecutable = numbers[1];
            IsTearDownTestSuite = values.Contains(TearDownTestSuiteMarker);
            IsBenchmark = values.Contains(BenchmarkMarker);
        }
    }
}
