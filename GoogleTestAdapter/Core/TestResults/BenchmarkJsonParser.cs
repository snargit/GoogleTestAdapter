using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoogleTestAdapter.TestResults
{

    /// <summary>
    /// A run of a benchmark as written by Google Benchmark with --benchmark_out_format=json: an iteration (one per
    /// repetition) or an aggregate of the repetitions (e.g. "BM_Foo_mean")
    /// </summary>
    public class BenchmarkRun
    {
        public string Name { get; set; }
        /// <summary>The name of the benchmark, i.e. without the aggregate's suffix</summary>
        public string RunName { get; set; }
        public bool IsAggregate { get; set; }
        public string AggregateName { get; set; }
        /// <summary>"time", or "percentage" for the coefficient of variation (cv)</summary>
        public string AggregateUnit { get; set; }
        public int Repetitions { get; set; }
        public int? RepetitionIndex { get; set; }
        public int Threads { get; set; }

        public bool ErrorOccurred { get; set; }
        public string ErrorMessage { get; set; }
        public bool Skipped { get; set; }
        public string SkipMessage { get; set; }

        public long Iterations { get; set; }
        public double? RealTime { get; set; }
        public double? CpuTime { get; set; }
        public string TimeUnit { get; set; }
        public string BigO { get; set; }
        public string Label { get; set; }
        /// <summary>User counters and the like (e.g. bytes_per_second), in the order of the file</summary>
        public IList<KeyValuePair<string, double>> Counters { get; } = new List<KeyValuePair<string, double>>();
    }

    /// <summary>
    /// Parses the JSON file written by Google Benchmark with --benchmark_out=&lt;file&gt; --benchmark_out_format=json.
    /// The file is written while the benchmarks are run, i.e., it is incomplete if the executable crashes or is
    /// killed; the numbers might be Infinity or NaN.
    /// </summary>
    public static class BenchmarkJsonParser
    {
        private static readonly HashSet<string> NonCounterNumbers = new HashSet<string>
        {
            "family_index", "per_family_instance_index", "repetitions", "repetition_index", "threads", "iterations",
            "real_time", "cpu_time"
        };

        /// <returns>The runs of the file's "benchmarks" array, up to the first incomplete one</returns>
        public static IList<BenchmarkRun> ParseRuns(string json)
        {
            var runs = new List<BenchmarkRun>();
            var reader = new Reader(json);
            try
            {
                reader.Expect('{');
                if (reader.TryConsume('}'))
                    return runs;
                do
                {
                    string key = reader.ReadString();
                    reader.Expect(':');
                    if (key != "benchmarks")
                    {
                        reader.ReadValue();
                        continue;
                    }

                    reader.Expect('[');
                    if (reader.TryConsume(']'))
                        continue;
                    do
                    {
                        if (reader.ReadValue() is List<KeyValuePair<string, object>> run)
                            runs.Add(CreateRun(run));
                    }
                    while (reader.TryConsume(','));
                    reader.Expect(']');
                }
                while (reader.TryConsume(','));
            }
            catch (FormatException)
            {
                // the file is incomplete
            }
            return runs;
        }

        private static BenchmarkRun CreateRun(List<KeyValuePair<string, object>> keyValues)
        {
            var values = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> keyValue in keyValues)
                values[keyValue.Key] = keyValue.Value;

            var run = new BenchmarkRun
            {
                Name = GetString(values, "name"),
                RunName = GetString(values, "run_name"),
                IsAggregate = GetString(values, "run_type") == "aggregate",
                AggregateName = GetString(values, "aggregate_name"),
                AggregateUnit = GetString(values, "aggregate_unit"),
                Repetitions = (int)(GetNumber(values, "repetitions") ?? 1),
                RepetitionIndex = (int?)GetNumber(values, "repetition_index"),
                Threads = (int)(GetNumber(values, "threads") ?? 1),
                ErrorOccurred = values.TryGetValue("error_occurred", out object error) && error is bool e && e,
                ErrorMessage = GetString(values, "error_message"),
                Skipped = values.TryGetValue("skipped", out object skipped) && skipped is bool s && s,
                SkipMessage = GetString(values, "skip_message"),
                Iterations = (long)(GetNumber(values, "iterations") ?? 0),
                RealTime = GetNumber(values, "real_time"),
                CpuTime = GetNumber(values, "cpu_time"),
                TimeUnit = GetString(values, "time_unit") ?? "ns",
                BigO = GetString(values, "big_o"),
                Label = GetString(values, "label")
            };
            run.RunName = run.RunName ?? run.Name;

            foreach (KeyValuePair<string, object> value in keyValues)
            {
                if (value.Value is double number && !NonCounterNumbers.Contains(value.Key))
                    run.Counters.Add(new KeyValuePair<string, double>(value.Key, number));
            }
            return run;
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            return values.TryGetValue(key, out object value) ? value as string : null;
        }

        private static double? GetNumber(Dictionary<string, object> values, string key)
        {
            return values.TryGetValue(key, out object value) && value is double number ? number : (double?)null;
        }

        private sealed class Reader
        {
            private readonly string _json;
            private int _position;

            public Reader(string json)
            {
                _json = json ?? "";
            }

            public void Expect(char c)
            {
                if (!TryConsume(c))
                    throw new FormatException($"Expected '{c}' at position {_position}");
            }

            public bool TryConsume(char c)
            {
                SkipWhitespace();
                if (_position < _json.Length && _json[_position] == c)
                {
                    _position++;
                    return true;
                }
                return false;
            }

            // objects are lists of key value pairs (in the order of the file), arrays are lists, numbers are doubles
            public object ReadValue()
            {
                SkipWhitespace();
                if (_position >= _json.Length)
                    throw new FormatException("Unexpected end of file");

                char c = _json[_position];
                switch (c)
                {
                    case '{':
                        _position++;
                        var keyValues = new List<KeyValuePair<string, object>>();
                        if (TryConsume('}'))
                            return keyValues;
                        do
                        {
                            string key = ReadString();
                            Expect(':');
                            keyValues.Add(new KeyValuePair<string, object>(key, ReadValue()));
                        }
                        while (TryConsume(','));
                        Expect('}');
                        return keyValues;
                    case '[':
                        _position++;
                        var list = new List<object>();
                        if (TryConsume(']'))
                            return list;
                        do
                        {
                            list.Add(ReadValue());
                        }
                        while (TryConsume(','));
                        Expect(']');
                        return list;
                    case '"':
                        return ReadString();
                    default:
                        if (TryReadLiteral("true"))
                            return true;
                        if (TryReadLiteral("false"))
                            return false;
                        if (TryReadLiteral("null"))
                            return null;
                        return ReadNumber();
                }
            }

            public string ReadString()
            {
                Expect('"');
                var result = new StringBuilder();
                while (true)
                {
                    if (_position >= _json.Length)
                        throw new FormatException("Unexpected end of file in string");

                    char c = _json[_position++];
                    if (c == '"')
                        return result.ToString();
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }

                    if (_position >= _json.Length)
                        throw new FormatException("Unexpected end of file in string");
                    char escaped = _json[_position++];
                    switch (escaped)
                    {
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _json.Length)
                                throw new FormatException("Unexpected end of file in string");
                            result.Append((char)int.Parse(_json.Substring(_position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _position += 4;
                            break;
                        default: result.Append(escaped); break;
                    }
                }
            }

            // Google Benchmark writes Infinity, -Infinity, and NaN, which JSON does not know
            private double ReadNumber()
            {
                if (TryReadLiteral("Infinity") || TryReadLiteral("inf"))
                    return double.PositiveInfinity;
                if (TryReadLiteral("-Infinity") || TryReadLiteral("-inf"))
                    return double.NegativeInfinity;
                if (TryReadLiteral("NaN") || TryReadLiteral("nan") || TryReadLiteral("-nan"))
                    return double.NaN;

                int start = _position;
                while (_position < _json.Length && "+-0123456789.eE".IndexOf(_json[_position]) >= 0)
                    _position++;
                // a number at the end of the file might be incomplete
                if (_position == start || _position >= _json.Length)
                    throw new FormatException($"Expected a value at position {start}");
                if (!double.TryParse(_json.Substring(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    throw new FormatException($"Invalid number at position {start}");
                return number;
            }

            private bool TryReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_json, _position, literal, 0, literal.Length) != 0)
                    return false;
                _position += literal.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_position < _json.Length && char.IsWhiteSpace(_json[_position]))
                    _position++;
            }
        }
    }

}
