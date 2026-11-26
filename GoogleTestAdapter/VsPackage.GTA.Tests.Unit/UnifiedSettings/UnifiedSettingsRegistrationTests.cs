using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.ProcessExecution;
using GoogleTestAdapter.Settings;
using GoogleTestAdapter.Tests.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.VsPackage.UnifiedSettings
{
    /// <summary>
    /// GTA's options in Visual Studio's Unified Settings (its new settings UI): the registration manifest is generated
    /// from the options pages (their sources, since the tests can not load the pages' types, whose base class is
    /// provided by Visual Studio), SettingsWrapper's texts and defaults, and the generated pkgdef. If the options
    /// change, run the tests with environment variable GTA_UPDATE_UNIFIED_SETTINGS=1, which writes the manifest.
    /// </summary>
    [TestClass]
    public class UnifiedSettingsRegistrationTests
    {
        private const string UpdateVariable = "GTA_UPDATE_UNIFIED_SETTINGS";
        private const string CategoryName = "Google Test Adapter";
        private const string RootMoniker = "googleTestAdapter";
        private const string ManifestPath = @"UnifiedSettings\GoogleTestAdapter.registration.json";
        // Visual Studio also writes changes back to DialogPage's store, which the options pages use when Unified
        // Settings are not active (e.g. VS 2022 without its preview feature, or after a downgrade of GTA)
        private const string MigrationMode = "full";

        private static readonly string SourceDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(UnifiedSettingsRegistrationTests).Assembly.Location) ?? "", TestResources.RootDir, "GoogleTestAdapter"));
        private static readonly string PackagingDir = Path.Combine(SourceDir, "Packaging.GTA");
        private static readonly string Manifest = Path.Combine(PackagingDir, ManifestPath);

        // in the order of the old options dialog
        private static readonly (string Name, string TypeName)[] Pages =
        {
            (SettingsWrapper.PageGeneralName, "GeneralOptionsDialogPage"),
            (SettingsWrapper.PageGoogleTestName, "GoogleTestOptionsDialogPage"),
            (SettingsWrapper.PageTestDiscovery, "TestDiscoveryOptionsDialogPage"),
            (SettingsWrapper.PageTestExecution, "TestExecutionOptionsDialogPage"),
        };

        private static readonly Type[] EnumTypes =
        {
            typeof(OutputMode), typeof(TimestampMode), typeof(SeverityMode), typeof(SummaryMode), typeof(DebuggerKind), typeof(MissingTestsReportMode)
        };

        private class Option
        {
            public string Page, PageType, Category, Property, Type, DisplayName, Description, Default, Moniker, Body;
            public readonly Dictionary<string, int> Constraints = new Dictionary<string, int>();
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Manifest_IsGeneratedFromOptionsPages()
        {
            string expected = GenerateManifest();
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                File.WriteAllText(Manifest, expected, new UTF8Encoding(false));
            }

            string actual = File.Exists(Manifest) ? File.ReadAllText(Manifest).Replace("\r\n", "\n") : "";
            actual.Should().Be(expected.Replace("\r\n", "\n"),
                $"{Manifest} must be generated from the options pages: run the tests with environment variable {UpdateVariable}=1");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Monikers_FollowPagesAndCategories()
        {
            var options = GetOptions();
            options.Should().HaveCount(39);
            options.Select(o => o.Moniker).Should().OnlyHaveUniqueItems();
            foreach (Option option in options)
            {
                // the moniker's segments are the page's, the category's, and the property's names
                string[] segments = option.Moniker.Split('.');
                segments.Should().HaveCount(4, option.Moniker);
                segments[0].Should().Be(RootMoniker);
                segments[1].Should().Be(ToMonikerSegment(option.PageType.Replace("OptionsDialogPage", "")), option.Moniker);
                segments[3].Should().Be(ToMonikerSegment(option.Property), option.Moniker);
            }
            // one category moniker per category of a page
            foreach (var page in options.GroupBy(o => o.Page))
            {
                page.GroupBy(o => o.Category).Select(c => c.Select(o => CategoryMoniker(o)).Distinct().Count()).Should().OnlyContain(n => n == 1);
                page.Select(o => CategoryMoniker(o)).Distinct().Should().HaveCount(page.Select(o => o.Category).Distinct().Count());
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Constraints_MatchValidationOfSetters()
        {
            foreach (Option option in GetOptions())
            {
                var expected = new Dictionary<string, int>();
                Match minimum = Regex.Match(option.Body, @"if \(value < (-?\d+)\)");
                if (minimum.Success)
                    expected["Minimum"] = int.Parse(minimum.Groups[1].Value);
                Match maxLength = Regex.Match(option.Body, @"if \(value\.Length > (\d+)\)");
                if (maxLength.Success)
                    expected["MaxLength"] = int.Parse(maxLength.Groups[1].Value);
                if (option.Body.Contains(nameof(GoogleTestConstants.ValidateShuffleTestsSeedValue)))
                {
                    expected["Minimum"] = GoogleTestConstants.ShuffleTestsSeedMinValue;
                    expected["Maximum"] = GoogleTestConstants.ShuffleTestsSeedMaxValue;
                }
                option.Constraints.Should().BeEquivalentTo(expected, $"the settings UI enforces the setter's limits of {option.Property}");
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Pkgdef_RegistersManifestAndMigratedPages()
        {
            string pkgdef = ReadPkgdef();
            pkgdef.Should().Contain(@"[$RootKey$\SettingsManifests\{E7C90FCB-0943-4908-9AE8-3B6A9D22EC9E}]");
            pkgdef.Should().Contain($@"""ManifestPath""=""$PackageFolder$\{ManifestPath}""");
            foreach (var page in Pages)
            {
                pkgdef.Should().Contain($"[$RootKey$\\ToolsOptionsPages\\{CategoryName}\\{page.Name}]\r\n\"IsInUnifiedSettings\"=dword:00000001");
                // the manifest refers to the page's GUID, which must not depend on the assembly's version (as it does
                // without GuidAttribute), since CI builds stamp their own versions
                string source = File.ReadAllText(Path.Combine(SourceDir, "VsPackage.Shared", "OptionsPages", page.TypeName + ".cs"));
                Match guid = Regex.Match(source, $@"\[Guid\(""([0-9a-f-]+)""\)\]\s*public class {page.TypeName}\b");
                guid.Success.Should().BeTrue($"{page.TypeName} has a GuidAttribute");
                GetPageGuid(pkgdef, page.Name).Should().Be(guid.Groups[1].Value);
            }

            string project = File.ReadAllText(Path.Combine(PackagingDir, "Packaging.GTA.csproj"));
            project.Should().MatchRegex(@"<Content Include=""UnifiedSettings\\GoogleTestAdapter\.registration\.json"">\s*<IncludeInVSIX>true</IncludeInVSIX>\s*<VSIXSubPath>UnifiedSettings</VSIXSubPath>");
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void SettingsReference_ExplainsMonikers()
        {
            string reference = File.ReadAllText(Path.Combine(SourceDir, "..", "agent-plugin", "skills", "google-test-adapter", "references", "settings.md"));
            reference.Should().Contain($"`{RootMoniker}.<page>.<group>.<DTE name, first letter lower-case>`");
            var monikers = GetOptions().Select(o => o.Moniker).ToList();
            var examples = Regex.Matches(reference, $@"`({RootMoniker}\.\w+\.\w+\.\w+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            examples.Should().NotBeEmpty();
            monikers.Should().Contain(examples, "the settings reference's examples are GTA's monikers");
        }

        private static string ReadPkgdef()
        {
            string file = Path.Combine(PackagingDir, "GoogleTestAdapter.VsPackage.GTA.pkgdef");
            File.Exists(file).Should().BeTrue($"the build of VsPackage.GTA generates {file}");
            return File.ReadAllText(file);
        }

        private static string GetPageGuid(string pkgdef, string page)
        {
            Match match = Regex.Match(pkgdef, $@"\[\$RootKey\$\\ToolsOptionsPages\\{Regex.Escape(CategoryName)}\\{Regex.Escape(page)}\][^\[]*""Page""=""\{{([0-9a-f-]+)\}}""");
            match.Success.Should().BeTrue($"the pkgdef registers page {page}");
            return match.Groups[1].Value;
        }

        private static string ToMonikerSegment(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static string CategoryMoniker(Option option) => option.Moniker.Substring(0, option.Moniker.LastIndexOf('.'));

        private static string PageMoniker(Option option) => string.Join(".", option.Moniker.Split('.').Take(2));

        private static object GetConstant(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            field.Should().NotBeNull($"{type.Name}.{name} exists");
            return field.GetValue(null);
        }

        private static int Evaluate(string expression)
        {
            expression = expression.Trim();
            if (int.TryParse(expression, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                return value;
            Match constant = Regex.Match(expression, @"^GoogleTestConstants\.(\w+)$");
            constant.Success.Should().BeTrue($"'{expression}' is a number or a constant of GoogleTestConstants");
            return (int)GetConstant(typeof(GoogleTestConstants), constant.Groups[1].Value);
        }

        private static List<Option> GetOptions()
        {
            var options = new List<Option>();
            foreach (var page in Pages)
            {
                string source = File.ReadAllText(Path.Combine(SourceDir, "VsPackage.Shared", "OptionsPages", page.TypeName + ".cs"));
                // each option starts with its category, and ends before the next one, or the page's end
                var starts = Regex.Matches(source, @"^\s*\[Category\(", RegexOptions.Multiline).Cast<Match>().Select(m => m.Index).ToList();
                starts.Should().NotBeEmpty($"{page.TypeName} has options");
                for (int i = 0; i < starts.Count; i++)
                {
                    string text = source.Substring(starts[i], (i + 1 < starts.Count ? starts[i + 1] : source.Length) - starts[i]);
                    Match declaration = Regex.Match(text, @"^\s*((?:\[[^\r\n]*\]\s*)+)public\s+(\w+)\s+(\w+)");
                    declaration.Success.Should().BeTrue($"an option of {page.TypeName} is declared as expected:\n{text}");
                    string attributes = declaration.Groups[1].Value;
                    string Attribute(string name)
                    {
                        Match match = Regex.Match(attributes, $@"\[{name}\((.*)\)\]");
                        match.Success.Should().BeTrue($"{page.TypeName}.{declaration.Groups[3].Value} has attribute {name}");
                        return match.Groups[1].Value;
                    }
                    string SettingsWrapperConstant(string name)
                    {
                        Match match = Regex.Match(Attribute(name), @"^SettingsWrapper\.(\w+)$");
                        match.Success.Should().BeTrue($"{name} of {page.TypeName}.{declaration.Groups[3].Value} is a constant of SettingsWrapper");
                        return (string)GetConstant(typeof(SettingsWrapper), match.Groups[1].Value);
                    }

                    var option = new Option
                    {
                        Page = page.Name,
                        PageType = page.TypeName,
                        Type = declaration.Groups[2].Value,
                        Property = declaration.Groups[3].Value,
                        Body = text,
                    };
                    option.Category = SettingsWrapperConstant("Category");
                    option.DisplayName = SettingsWrapperConstant("DisplayName");
                    option.Description = SettingsWrapperConstant("Description");
                    Match field = Regex.Match(text, @"get\s*(?:=>|\{\s*return)\s*(_\w+)\s*;");
                    field.Success.Should().BeTrue($"{page.TypeName}.{option.Property} returns a field");
                    Match fieldDefault = Regex.Match(text, $@"private\s+\w+\s+{field.Groups[1].Value}\s*=\s*SettingsWrapper\.(\w+)\s*;");
                    fieldDefault.Success.Should().BeTrue($"{page.TypeName}.{option.Property} is initialized with a default of SettingsWrapper");
                    option.Default = fieldDefault.Groups[1].Value;

                    Match setting = Regex.Match(Attribute("UnifiedSetting"), @"^""([^""]+)""((?:\s*,\s*\w+\s*=\s*[^,]+)*)$");
                    setting.Success.Should().BeTrue($"{page.TypeName}.{option.Property} has a moniker");
                    option.Moniker = setting.Groups[1].Value;
                    foreach (Match constraint in Regex.Matches(setting.Groups[2].Value, @"(\w+)\s*=\s*([^,]+)"))
                    {
                        option.Constraints[constraint.Groups[1].Value] = Evaluate(constraint.Groups[2].Value);
                    }
                    options.Add(option);
                }
            }
            return options;
        }

        private static Type GetEnumType(Option option)
        {
            Type type = EnumTypes.SingleOrDefault(t => t.Name == option.Type);
            type.Should().NotBeNull($"{option.Property} has type bool, int, string, or one of the known enumerations");
            return type;
        }

        private static string ToEnumString(Type enumType, object value) => ToMonikerSegment(Enum.GetName(enumType, value));

        private static string GenerateManifest()
        {
            string pkgdef = ReadPkgdef();
            List<Option> options = GetOptions();

            var properties = new JsonObject();
            foreach (var category in options.GroupBy(CategoryMoniker))
            {
                int order = 0;
                foreach (Option option in category)
                {
                    var registration = new JsonObject();
                    object defaultValue = GetConstant(typeof(SettingsWrapper), option.Default);
                    var input = new JsonObject
                    {
                        { "store", "SettingsManager" },
                        // where DialogPage stores the option's value
                        { "path", $"GoogleTestAdapter.VsPackage.OptionsPages.{option.PageType}.{option.Property}" }
                    };
                    var migration = new JsonObject();
                    switch (option.Type)
                    {
                        case "bool":
                            registration.Add("type", "boolean");
                            registration.Add("title", option.DisplayName);
                            registration.Add("description", option.Description);
                            registration.Add("default", (bool)defaultValue);
                            migration.Add("pass", new JsonObject { { "input", input }, { "mode", MigrationMode } });
                            break;
                        case "int":
                            registration.Add("type", "integer");
                            registration.Add("title", option.DisplayName);
                            registration.Add("description", option.Description);
                            registration.Add("default", (int)defaultValue);
                            if (option.Constraints.TryGetValue("Minimum", out int minimum))
                                registration.Add("minimum", minimum);
                            if (option.Constraints.TryGetValue("Maximum", out int maximum))
                                registration.Add("maximum", maximum);
                            migration.Add("pass", new JsonObject { { "input", input }, { "mode", MigrationMode } });
                            break;
                        case "string":
                            registration.Add("type", "string");
                            registration.Add("title", option.DisplayName);
                            registration.Add("description", option.Description);
                            registration.Add("default", (string)defaultValue);
                            if (option.Constraints.TryGetValue("MaxLength", out int maxLength))
                                registration.Add("maxLength", maxLength);
                            migration.Add("pass", new JsonObject { { "input", input }, { "mode", MigrationMode } });
                            break;
                        default:
                            Type enumType = GetEnumType(option);
                            TypeConverter converter = TypeDescriptor.GetConverter(enumType);
                            var values = Enum.GetValues(enumType).Cast<object>().ToList();
                            registration.Add("type", "string");
                            registration.Add("title", option.DisplayName);
                            registration.Add("description", option.Description);
                            registration.Add("default", ToEnumString(enumType, defaultValue));
                            registration.Add("enum", values.Select(v => (object)ToEnumString(enumType, v)).ToList());
                            registration.Add("enumItemLabels", values.Select(v => (object)converter.ConvertToString(v)).ToList());
                            // DialogPage stores the enumeration's value
                            migration.Add("enumIntegerToString", new JsonObject
                            {
                                { "input", input },
                                { "map", values.Select(v => (object)new JsonObject { { "result", ToEnumString(enumType, v) }, { "match", Convert.ToInt32(v) } }).ToList() },
                                { "mode", MigrationMode }
                            });
                            break;
                    }
                    if (option.Constraints.Keys.Except(new[] { "Minimum", "Maximum", "MaxLength" }).Any())
                        throw new InvalidOperationException($"Unknown constraint of {option.Property}");
                    // values can't be set per solution: GTA's solution settings files do that
                    registration.Add("transientScopesSupported", false);
                    registration.Add("order", order++);
                    registration.Add("migration", migration);
                    properties.Add(option.Moniker, registration);
                }
            }

            var categories = new JsonObject { { RootMoniker, new JsonObject { { "title", CategoryName } } } };
            int pageOrder = 0;
            foreach (var page in options.GroupBy(PageMoniker))
            {
                string pageName = page.First().Page;
                categories.Add(page.Key, new JsonObject
                {
                    { "title", pageName },
                    { "order", pageOrder++ },
                    // the page shown before, which Visual Studio hides
                    { "legacyOptionPageId", GetPageGuid(pkgdef, pageName) }
                });
                int categoryOrder = 0;
                foreach (var category in page.GroupBy(CategoryMoniker))
                {
                    categories.Add(category.Key, new JsonObject { { "title", category.First().Category }, { "order", categoryOrder++ } });
                }
            }

            var builder = new StringBuilder();
            builder.Append("// Google Test Adapter's options in Visual Studio's Unified Settings. Generated by UnifiedSettingsRegistrationTests\n");
            builder.Append("// from the options pages: don't edit, run the tests with environment variable " + UpdateVariable + "=1 instead.\n");
            WriteJson(builder, new JsonObject { { "properties", properties }, { "categories", categories } }, 0);
            builder.Append("\n");
            return builder.ToString();
        }

        private class JsonObject : List<KeyValuePair<string, object>>
        {
            public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
        }

        private static void WriteJson(StringBuilder builder, object value, int indent)
        {
            string Indent(int level) => new string(' ', 2 * level);
            switch (value)
            {
                case JsonObject obj:
                    builder.Append("{\n");
                    for (int i = 0; i < obj.Count; i++)
                    {
                        builder.Append(Indent(indent + 1));
                        WriteString(builder, obj[i].Key);
                        builder.Append(": ");
                        WriteJson(builder, obj[i].Value, indent + 1);
                        builder.Append(i + 1 < obj.Count ? ",\n" : "\n");
                    }
                    builder.Append(Indent(indent)).Append("}");
                    break;
                case IList list:
                    if (list.Cast<object>().All(item => item is string))
                    {
                        builder.Append("[ ");
                        for (int i = 0; i < list.Count; i++)
                        {
                            WriteString(builder, (string)list[i]);
                            builder.Append(i + 1 < list.Count ? ", " : " ");
                        }
                        builder.Append("]");
                        break;
                    }
                    builder.Append("[\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        builder.Append(Indent(indent + 1));
                        WriteJson(builder, list[i], indent + 1);
                        builder.Append(i + 1 < list.Count ? ",\n" : "\n");
                    }
                    builder.Append(Indent(indent)).Append("]");
                    break;
                case string s:
                    WriteString(builder, s);
                    break;
                case bool b:
                    builder.Append(b ? "true" : "false");
                    break;
                case int n:
                    builder.Append(n.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"Unsupported JSON value {value}");
            }
        }

        private static void WriteString(StringBuilder builder, string s)
        {
            builder.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e)
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
