namespace GoogleTestAdapter
{
    /// <summary>
    /// The test frameworks of the executables GTA discovers tests in; executables containing both Google Test tests
    /// and Google Benchmark benchmarks are treated as Google Test executables
    /// </summary>
    public enum TestFramework { GoogleTest, GoogleBenchmark }

    public static class GoogleBenchmarkConstants
    {
        public const string ListTestsOption = "--benchmark_list_tests";

        // the trait of all benchmarks, e.g. for grouping or filtering them in Test Explorer
        public const string TraitName = "Type";
        public const string TraitValue = "Benchmark";

        // the class Google Benchmark derives from a fixture for each benchmark defined with BENCHMARK_F(Fixture,
        // Method) and BENCHMARK_DEFINE_F(): Fixture_Method_Benchmark, whose method BenchmarkCase() is the benchmark
        public const string FixtureClassSuffix = "_Benchmark";
        public const string FixtureMethod = "BenchmarkCase";

        // debug builds might use a postfix (e.g. CMAKE_DEBUG_POSTFIX "d")
        public static readonly string[] GoogleBenchmarkDllMarkers =
        {
            "benchmark.dll", "benchmarkd.dll",
            "benchmark_main.dll", "benchmark_maind.dll"
        };

        // the names of Google Benchmark's flags and a message of its benchmark filter
        public static readonly string[] GoogleBenchmarkExecutableMarkers =
        {
            "benchmark_list_tests",
            "benchmark_filter",
            "benchmark_out_format",
            "Failed to match any benchmarks against regex"
        };
    }
}
