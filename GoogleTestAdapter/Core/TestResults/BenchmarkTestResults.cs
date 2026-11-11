using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GoogleTestAdapter.Model;

namespace GoogleTestAdapter.TestResults
{

    /// <summary>
    /// The test results of benchmarks: a benchmark fails if it reports an error (State::SkipWithError()), is skipped
    /// if it skips itself (State::SkipWithMessage()), and passes otherwise. The result's error message summarizes
    /// the measurement (e.g. "12.3 ns (CPU 12.1 ns), 56M iterations"), its output contains the statistics of all
    /// runs (including counters and the aggregates of repetitions).
    /// </summary>
    public static class BenchmarkTestResults
    {
        public const string DryRunPrefix = "Dry run: ";

        public static TestResult CreateTestResult(TestCase testCase, IList<BenchmarkRun> runs, bool isDryRun)
        {
            var result = new TestResult(testCase)
            {
                ComputerName = Environment.MachineName,
                DisplayName = testCase.DisplayName,
                Duration = GetDuration(runs),
                StandardOutput = GetStatistics(runs, isDryRun)
            };

            BenchmarkRun error = runs.FirstOrDefault(r => r.ErrorOccurred);
            BenchmarkRun skipped = runs.FirstOrDefault(r => r.Skipped);
            if (error != null)
            {
                result.Outcome = TestOutcome.Failed;
                result.ErrorMessage = string.IsNullOrEmpty(error.ErrorMessage) ? "Benchmark reported an error" : error.ErrorMessage;
                result.ErrorStackTrace = GetLocationStackTrace(testCase);
            }
            else if (skipped != null)
            {
                result.Outcome = TestOutcome.Skipped;
                result.ErrorMessage = skipped.SkipMessage;
            }
            else
            {
                result.Outcome = TestOutcome.Passed;
                result.ErrorMessage = GetSummary(runs, isDryRun);
            }
            return result;
        }

        // e.g. "12.3 ns (CPU 12.1 ns), 56M iterations", or the mean of repetitions
        public static string GetSummary(IList<BenchmarkRun> runs, bool isDryRun)
        {
            List<BenchmarkRun> iterations = runs.Where(r => !r.IsAggregate).ToList();
            BenchmarkRun mean = runs.FirstOrDefault(r => r.IsAggregate && r.AggregateName == "mean");
            BenchmarkRun run = mean ?? iterations.FirstOrDefault();
            if (run == null)
                return null;

            string summary = $"{FormatTime(run.RealTime, run.TimeUnit)} (CPU {FormatTime(run.CpuTime, run.TimeUnit)})";
            if (mean != null && iterations.Count > 0)
                summary += $", mean of {iterations.Count} repetitions of {FormatIterations(iterations[0].Iterations)}";
            else
                summary += $", {FormatIterations(run.Iterations)}";
            return isDryRun ? DryRunPrefix + summary : summary;
        }

        public static string GetStatistics(IList<BenchmarkRun> runs, bool isDryRun)
        {
            var statistics = new StringBuilder();
            if (isDryRun)
                statistics.AppendLine("Dry run (--benchmark_dry_run): each benchmark is run for one iteration, the times are not meaningful");

            foreach (BenchmarkRun run in runs)
            {
                string header = run.Name;
                if (!run.IsAggregate && run.Repetitions > 1 && run.RepetitionIndex.HasValue)
                    header += $" (repetition {run.RepetitionIndex.Value + 1} of {run.Repetitions})";
                statistics.AppendLine(header);

                if (run.ErrorOccurred)
                    statistics.AppendLine($"  Error: {run.ErrorMessage}");
                if (run.Skipped)
                    statistics.AppendLine($"  Skipped: {run.SkipMessage}");
                if (run.IsAggregate && run.AggregateUnit == "percentage")
                {
                    statistics.AppendLine($"  Real time: {FormatPercentage(run.RealTime)}");
                    statistics.AppendLine($"  CPU time: {FormatPercentage(run.CpuTime)}");
                }
                else if (run.BigO != null)
                {
                    statistics.AppendLine($"  Real time: {FormatNumber(run.RealTime ?? double.NaN)} {run.BigO}");
                    statistics.AppendLine($"  CPU time: {FormatNumber(run.CpuTime ?? double.NaN)} {run.BigO}");
                }
                else
                {
                    if (run.RealTime.HasValue)
                        statistics.AppendLine($"  Real time: {FormatTime(run.RealTime, run.TimeUnit)}");
                    if (run.CpuTime.HasValue)
                        statistics.AppendLine($"  CPU time: {FormatTime(run.CpuTime, run.TimeUnit)}");
                }
                if (!run.IsAggregate)
                    statistics.AppendLine($"  Iterations: {run.Iterations.ToString(CultureInfo.InvariantCulture)}");
                if (run.Threads > 1)
                    statistics.AppendLine($"  Threads: {run.Threads.ToString(CultureInfo.InvariantCulture)}");
                foreach (KeyValuePair<string, double> counter in run.Counters)
                    statistics.AppendLine($"  {counter.Key}: {FormatNumber(counter.Value)}");
                if (!string.IsNullOrEmpty(run.Label))
                    statistics.AppendLine($"  Label: {run.Label}");
            }
            return statistics.ToString();
        }

        // the time measured by the iterations of all repetitions; the time of setting up the benchmark (and of
        // finding the number of iterations) is not known
        private static TimeSpan GetDuration(IEnumerable<BenchmarkRun> runs)
        {
            double seconds = runs
                .Where(r => !r.IsAggregate && r.RealTime.HasValue && !double.IsNaN(r.RealTime.Value) && !double.IsInfinity(r.RealTime.Value))
                .Sum(r => r.RealTime.Value * GetSecondsPerUnit(r.TimeUnit) * r.Iterations);
            return TimeSpan.FromTicks((long)Math.Round(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2) * TimeSpan.TicksPerSecond));
        }

        private static double GetSecondsPerUnit(string timeUnit)
        {
            switch (timeUnit)
            {
                case "s": return 1;
                case "ms": return 1e-3;
                case "us": return 1e-6;
                default: return 1e-9;
            }
        }

        public static string GetLocationStackTrace(TestCase testCase)
        {
            return string.IsNullOrEmpty(testCase.CodeFilePath)
                ? ""
                : ErrorMessageParser.CreateStackTraceEntry(testCase.FullyQualifiedName, testCase.CodeFilePath, testCase.LineNumber.ToString());
        }

        public static string FormatTime(double? time, string timeUnit)
        {
            return time.HasValue ? $"{FormatNumber(time.Value, false)} {timeUnit}" : "?";
        }

        private static string FormatPercentage(double? fraction)
        {
            return fraction.HasValue ? $"{FormatNumber(fraction.Value * 100, false)} %" : "?";
        }

        public static string FormatIterations(long iterations)
        {
            return $"{FormatCount(iterations)} {(iterations == 1 ? "iteration" : "iterations")}";
        }

        // e.g. 9999, 56k, 1.5M
        public static string FormatCount(long count)
        {
            if (count < 10000)
                return count.ToString(CultureInfo.InvariantCulture);
            return FormatNumber(count);
        }

        /// <returns>The number with three significant digits (but all digits before the decimal point), with an SI
        /// suffix (k, M, G, T) for large numbers if <paramref name="useSuffixes"/> is set</returns>
        public static string FormatNumber(double value, bool useSuffixes = true)
        {
            if (double.IsNaN(value))
                return "nan";
            if (double.IsInfinity(value))
                return value > 0 ? "inf" : "-inf";

            string suffix = "";
            if (useSuffixes)
            {
                string[] suffixes = { "k", "M", "G", "T" };
                for (int i = 0; i < suffixes.Length && Math.Abs(value) >= 1000; i++)
                {
                    value /= 1000;
                    suffix = suffixes[i];
                }
            }

            double absolute = Math.Abs(value);
            string format = absolute == 0 || absolute >= 100 ? "0" : absolute >= 10 ? "0.#" : absolute >= 1 ? "0.##" : "G3";
            return value.ToString(format, CultureInfo.InvariantCulture) + suffix;
        }
    }

}
