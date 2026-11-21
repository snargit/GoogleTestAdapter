using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using FluentAssertions;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.ProcessExecution;
using GoogleTestAdapter.Settings;
using GoogleTestAdapter.Tests.Common;
using GoogleTestAdapter.VsPackage.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GoogleTestAdapter.Tests.Common.TestMetadata.TestCategories;

namespace GoogleTestAdapter.VsPackage.AgentPlugin
{
    /// <summary>
    /// The agent plugin in agent-plugin/ (with its skill) must agree with GTA: its manifests with GTA's version, the
    /// settings reference with GTA's settings, and the script with GTA's options and automation object.
    /// </summary>
    [TestClass]
    public class AgentPluginTests
    {
        private const string PluginName = "google-test-adapter";
        private const string SkillName = "google-test-adapter";

        private static readonly string RepositoryDir = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(AgentPluginTests).Assembly.Location) ?? "", TestResources.RootDir));
        private static readonly string PluginDir = Path.Combine(RepositoryDir, "agent-plugin");
        private static readonly string SkillDir = Path.Combine(PluginDir, "skills", SkillName);

        private static Dictionary<string, object> ReadJson(string file)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
        }

        private static string GtaVersion()
        {
            XDocument nuspec = XDocument.Load(Path.Combine(RepositoryDir, "GoogleTestAdapter", "Packaging.GTA", "VsPackage.nuspec"));
            return nuspec.Descendants().Single(e => e.Name.LocalName == "version").Value;
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Manifests_AgreeWithEachOtherAndWithGta()
        {
            var claude = ReadJson(Path.Combine(PluginDir, ".claude-plugin", "plugin.json"));
            var codex = ReadJson(Path.Combine(PluginDir, ".codex-plugin", "plugin.json"));
            var marketplace = ReadJson(Path.Combine(RepositoryDir, ".claude-plugin", "marketplace.json"));
            var entry = ((ArrayList)marketplace["plugins"]).Cast<Dictionary<string, object>>().Single();

            claude["name"].Should().Be(PluginName);
            codex["name"].Should().Be(PluginName);
            entry["name"].Should().Be(PluginName);
            codex["description"].Should().Be(claude["description"]);
            entry["description"].Should().Be(claude["description"]);
            // the version decides whether agents update the plugin: it is GTA's, and only in the plugin's manifests
            claude["version"].Should().Be(GtaVersion());
            codex["version"].Should().Be(GtaVersion());
            entry.Should().NotContainKey("version");
            Path.GetFullPath(Path.Combine(RepositoryDir, (string)entry["source"])).Should().Be(PluginDir);
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Skill_FollowsTheAgentSkillsStandard()
        {
            Directory.GetDirectories(Path.Combine(PluginDir, "skills")).Select(Path.GetFileName).Should().Equal(SkillName);

            string text = File.ReadAllText(Path.Combine(SkillDir, "SKILL.md"));
            Match frontMatter = Regex.Match(text, "^---\r?\n(.*?)\r?\n---\r?\n", RegexOptions.Singleline);
            frontMatter.Success.Should().BeTrue("SKILL.md starts with its front matter");
            var fields = frontMatter.Groups[1].Value.Split('\n')
                .Select(line => line.TrimEnd('\r').Split(new[] { ": " }, 2, StringSplitOptions.None))
                .ToDictionary(parts => parts[0], parts => parts[1]);
            // only the standard's required fields, which all agents understand
            fields.Keys.Should().BeEquivalentTo("name", "description");
            fields["name"].Should().Be(SkillName).And.MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
            fields["name"].Length.Should().BeLessOrEqualTo(64);
            fields["description"].Length.Should().BeLessOrEqualTo(1024);
            fields["description"].Should().Contain("Use when");
            text.Split('\n').Length.Should().BeLessThan(500);
            // nothing particular to one agent: the same folder is used by all of them
            text.Should().NotContain("${CLAUDE").And.NotContain("$CODEX");

            foreach (Match reference in Regex.Matches(text, @"`((?:scripts|references)/[^`\s]+)`"))
            {
                File.Exists(Path.Combine(SkillDir, reference.Groups[1].Value)).Should().BeTrue($"SKILL.md refers to {reference.Value}");
            }
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void SettingsReference_ListsAllSettingsAndOptions()
        {
            string reference = File.ReadAllText(Path.Combine(SkillDir, "references", "settings.md"));

            XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
            XElement settingsType = XDocument.Load(Path.Combine(RepositoryDir, "GoogleTestAdapter", "TestAdapter", "GoogleTestAdapterSettings.xsd"))
                .Descendants(xsd + "complexType").Single(t => (string)t.Attribute("name") == "SettingsType");
            var elements = settingsType.Descendants(xsd + "element").Select(e => (string)e.Attribute("name")).ToList();
            elements.Should().Contain("TestDiscoveryRegex");
            foreach (string element in elements)
            {
                reference.Should().Contain($"`<{element}>`", "the settings reference lists every element of the settings schema");
            }

            foreach (string option in GetOptions().SelectMany(page => page.Value.Keys))
            {
                reference.Should().Contain($"`{option}`", "the settings reference lists the DTE name of every option");
            }
        }

        // the options as DTE.Properties("Google Test Adapter", page) provides them: the properties of the options pages
        // with a category, read from the sources (the pages' base class is provided by Visual Studio, so the tests can not
        // load the pages' types), by page, with the names of their types
        private static Dictionary<string, Dictionary<string, string>> GetOptions()
        {
            var pages = new Dictionary<string, string>
            {
                { SettingsWrapper.PageGeneralName, "GeneralOptionsDialogPage.cs" },
                { SettingsWrapper.PageTestDiscovery, "TestDiscoveryOptionsDialogPage.cs" },
                { SettingsWrapper.PageTestExecution, "TestExecutionOptionsDialogPage.cs" },
                { SettingsWrapper.PageGoogleTestName, "GoogleTestOptionsDialogPage.cs" }
            };
            return pages.ToDictionary(page => page.Key, page =>
            {
                string source = File.ReadAllText(Path.Combine(RepositoryDir, "GoogleTestAdapter", "VsPackage.Shared", "OptionsPages", page.Value));
                var options = Regex.Matches(source, @"\[Category\([^)]*\)\]\s*(?:\[[^\r\n]*\]\s*)*public\s+(\w+)\s+(\w+)").Cast<Match>()
                    .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);
                options.Should().NotBeEmpty($"{page.Value} has options");
                return options;
            });
        }

        [TestMethod]
        [TestCategory(Unit)]
        public void Script_AgreesWithGtasOptionsAndAutomationObject()
        {
            string script = File.ReadAllText(Path.Combine(SkillDir, "scripts", "gta.ps1"));
            // Windows PowerShell reads scripts without byte order mark as ANSI
            script.Where(c => c >= 128).Should().BeEmpty("the script must only contain ASCII characters");

            List<string> pages = Regex.Match(script, @"\$optionPages = (.*)").Groups[1].Value
                .Split(',').Select(p => p.Trim().Trim('"')).ToList();
            pages.Should().BeEquivalentTo(GetOptions().Keys);

            var enumTypes = new[] { typeof(OutputMode), typeof(TimestampMode), typeof(SeverityMode), typeof(SummaryMode), typeof(DebuggerKind), typeof(MissingTestsReportMode) };
            var enums = Regex.Matches(script, @"^\s*""(\w+)"" = @\{ ([^}]*) \}", RegexOptions.Multiline).Cast<Match>()
                .ToDictionary(m => m.Groups[1].Value, m => Regex.Matches(m.Groups[2].Value, @"(\w+) = (\d+)").Cast<Match>()
                    .ToDictionary(v => v.Groups[1].Value, v => int.Parse(v.Groups[2].Value)));
            enums.Keys.Should().BeEquivalentTo(enumTypes.Select(t => t.Name));
            foreach (Type type in enumTypes)
            {
                enums[type.Name].Should().BeEquivalentTo(
                    Enum.GetValues(type).Cast<object>().ToDictionary(v => v.ToString(), v => (int)v), $"the script's values of {type.Name} are GTA's");
            }
            // the options with enumeration values are named like their types
            var simpleTypes = new[] { "bool", "int", "string" };
            GetOptions().SelectMany(page => page.Value).Where(option => !simpleTypes.Contains(option.Value))
                .Should().OnlyContain(option => option.Key == option.Value && enums.ContainsKey(option.Key));

            var automationMethods = typeof(IGoogleTestAdapterAutomation).GetMethods().Select(m => m.Name).ToList();
            foreach (Match call in Regex.Matches(script, @"Invoke-Gta \S+ ""(\w+)"""))
            {
                automationMethods.Should().Contain(call.Groups[1].Value, "the script calls methods of GTA's automation object");
            }
            foreach (Match method in Regex.Matches(Regex.Match(script, @"\$methodVersions = @\{(.*)\}").Groups[1].Value, @"""(\w+)"" = ""([\d.]+)"""))
            {
                automationMethods.Should().Contain(method.Groups[1].Value);
                (new Version(method.Groups[2].Value) > new Version(0, 21, 0)).Should().BeTrue(
                    "the methods of GTA 0.21.0 need no version check");
            }
        }
    }
}
