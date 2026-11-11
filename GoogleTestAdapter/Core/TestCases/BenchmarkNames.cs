using System;
using System.Collections.Generic;
using System.Linq;

namespace GoogleTestAdapter.TestCases
{

    /// <summary>
    /// The parts of the names of Google Benchmark benchmarks, e.g. "BM_Foo", "BM_Foo/8", "BM_Foo/size:8/threads:2",
    /// "BM_Foo&lt;int&gt;/8", "ns::BM_Foo", "Fixture/Method/8" (see <see cref="BenchmarkLocationResolver"/>)
    /// </summary>
    public static class BenchmarkNames
    {
        /// <returns>The parts of the name separated by '/' outside of template arguments</returns>
        public static IList<string> Split(string benchmarkName)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < benchmarkName.Length; i++)
            {
                switch (benchmarkName[i])
                {
                    case '<': depth++; break;
                    case '>': if (depth > 0) depth--; break;
                    case '/':
                        if (depth == 0)
                        {
                            parts.Add(benchmarkName.Substring(start, i - start));
                            start = i + 1;
                        }
                        break;
                }
            }
            parts.Add(benchmarkName.Substring(start));
            return parts;
        }

        /// <returns>The class Google Benchmark defines for a benchmark of a fixture, e.g. "Fixture_Method_Benchmark"
        /// for "Fixture/Method" and "Fixture&lt;int&gt;/Method" (BENCHMARK_TEMPLATE_F)</returns>
        public static string GetFixtureClass(string fixture, string method)
        {
            return $"{RemoveTemplateArguments(fixture)}_{method}{GoogleBenchmarkConstants.FixtureClassSuffix}";
        }

        /// <returns>The name without its trailing template arguments, e.g. "BM_Foo" for "BM_Foo&lt;int, 3&gt;"</returns>
        public static string RemoveTemplateArguments(string name)
        {
            if (!name.EndsWith(">", StringComparison.Ordinal))
                return name;

            int depth = 0;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                if (name[i] == '>')
                    depth++;
                else if (name[i] == '<' && --depth == 0)
                    return name.Substring(0, i);
            }
            return name;
        }

        /// <returns>The name without its namespaces, e.g. "BM_Foo&lt;std::string&gt;" for
        /// "ns::BM_Foo&lt;std::string&gt;"</returns>
        public static string RemoveNamespace(string qualifiedName)
        {
            int separator = IndexOfLastScopeSeparator(qualifiedName);
            return separator < 0 ? qualifiedName : qualifiedName.Substring(separator + 2);
        }

        /// <returns>The namespace of the qualified name, e.g. "outer::(anonymous namespace)" for
        /// "outer::`anonymous namespace'::BM_Foo", or "" for the global namespace</returns>
        public static string GetNamespace(string qualifiedName)
        {
            int separator = IndexOfLastScopeSeparator(qualifiedName);
            return separator < 0 ? "" : TestCaseResolver.GetNamespace(qualifiedName.Substring(0, separator + 2));
        }

        // the last "::" outside of template arguments
        private static int IndexOfLastScopeSeparator(string qualifiedName)
        {
            int depth = 0;
            for (int i = qualifiedName.Length - 1; i > 0; i--)
            {
                char c = qualifiedName[i];
                if (c == '>')
                    depth++;
                else if (c == '<' && depth > 0)
                    depth--;
                else if (depth == 0 && c == ':' && qualifiedName[i - 1] == ':')
                    return i - 1;
            }
            return -1;
        }

        /// <returns>The descriptor of the benchmark; Test Explorer shows benchmarks of fixtures in the fixture and the
        /// method, and benchmarks of functions in the function and its template instance, i.e., e.g. "BM_Foo/8" and
        /// "BM_Foo/64" in "BM_Foo" and "BM_Foo", and "BM_Bar&lt;int&gt;" in "BM_Bar" and "BM_Bar&lt;int&gt;"</returns>
        public static TestCaseDescriptor CreateDescriptor(string benchmarkName, bool isFixtureBenchmark)
        {
            IList<string> parts = Split(benchmarkName);
            string function = RemoveNamespace(parts[0]);
            string testClass = RemoveTemplateArguments(function);
            string testGroup = isFixtureBenchmark && parts.Count >= 2 ? parts[1] : function;
            return new TestCaseDescriptor(parts[0], parts.Count >= 2 ? string.Join("/", parts.Skip(1)) : parts[0],
                benchmarkName, benchmarkName, TestCaseDescriptor.TestTypes.Simple, testClass, testGroup);
        }
    }

}
