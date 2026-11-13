using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace AgentBench.Tests
{
    /// <summary>
    /// Tier 1: for every task in every suite, the untouched setup must fail the checker and the reference
    /// solution must pass it. Runs without a model, so it belongs in every test run: a tool change that breaks
    /// a creator workflow, or a checker that accepts anything, fails here before any agent is involved.
    /// </summary>
    public class ReferenceSolutionTests
    {
        static IEnumerable<TestCaseData> Tasks() =>
            BenchSuite.All().SelectMany(s => s.CreateTasks().Select(t => new TestCaseData(s.QualifiedId(t)).SetName($"Replay({s.QualifiedId(t)})")));

        static IEnumerable<TestCaseData> Suites() =>
            BenchSuite.All().Select(s => new TestCaseData(s.Name).SetName($"Tools({s.Name})"));

        [TearDown]
        public void TearDown() => BenchRunner.Teardown();

        [TestCaseSource(nameof(Tasks))]
        public void ReferenceSolution_PassesTheChecker(string taskId)
        {
            var report = BenchRunner.Replay(taskId, discardUnsaved: true);

            Assert.That(report.Before.Passed, Is.False, "the checker passes on the untouched setup, so the task tests nothing");
            foreach (var call in report.Calls)
                Assert.That(call.Ok, Is.True, $"{call.Tool} failed: {call.Error}");
            Assert.That(report.After.Passed, Is.True, report.After.ToString());
        }

        [TestCaseSource(nameof(Suites))]
        public void SuiteTools_AreDescribedForTheAgent(string suiteName)
        {
            var suite = BenchSuite.Find(suiteName);
            var tools = ToolCatalog.Find(suite.ToolIdPrefix);

            Assert.That(tools, Is.Not.Empty, $"no [AgentTool] ids start with '{suite.ToolIdPrefix}'");
            foreach (var tool in tools)
            {
                Assert.That(tool.Description, Is.Not.Empty, tool.Id);
                var properties = (Newtonsoft.Json.Linq.JObject)tool.InputSchema["properties"];
                foreach (var p in properties.Properties())
                    Assert.That((string)p.Value["description"], Is.Not.Null.And.Not.Empty, $"{tool.Id}.{p.Name} has no [ToolParameter] description");
            }
        }
    }
}
