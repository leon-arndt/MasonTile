using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace AgentBench.Pipeline
{
    /// <summary>
    /// The bench over the Pipeline server: <c>unity command agentbench &lt;name&gt;</c>, or POST /api/exec.
    /// The tier 2 harness (Harness~/agentbench.py) drives a task through setup, call (once per agent tool call),
    /// check and teardown. Thin on purpose: the logic lives in AgentBench.Editor, which the tests cover.
    /// </summary>
    static class BenchCommands
    {
        const string Group = "agentbench";

        [CliCommand(Group, "list", "List the bench suites and their tasks.")]
        static object List() =>
            BenchSuite.All().Select(s => new
            {
                suite = s.Name,
                toolPrefix = s.ToolIdPrefix,
                tasks = s.CreateTasks().Select(t => new { id = s.QualifiedId(t), tags = t.Tags, prompt = t.Prompt }).ToList(),
            }).ToList();

        [CliCommand(Group, "tools", "The suite's agent tools as name, description and JSON input schema.")]
        static object Tools([CliArg("suite", "Suite name, e.g. masontile.")] string suite)
        {
            var s = BenchSuite.Find(suite);
            return new
            {
                suite = s.Name,
                systemPrompt = s.SystemPrompt,
                tools = ToolCatalog.Find(s.ToolIdPrefix).Select(t => new { id = t.Id, description = t.Description, inputSchema = t.InputSchema }).ToList(),
            };
        }

        [CliCommand(Group, "setup", "Open an empty scene and build the task's starting state. Refuses if open scenes have unsaved changes.")]
        static object Setup(
            [CliArg("task", "Qualified task id, e.g. masontile/center-pool.")] string task,
            [CliArg("agent", "Who solves the task, shown in the Agent Bench window, e.g. 'Claude Code · sonnet'.")] string agent = "")
        {
            var (suite, t) = BenchRunner.Setup(task, agent: agent);
            return new { task, prompt = t.Prompt, systemPrompt = suite.SystemPrompt, tags = t.Tags };
        }

        [CliCommand(Group, "call", "Call an agent tool in the bench scene, as AI Assistant would, with permissions granted.")]
        static ToolCallOutcome Call(
            [CliArg("tool", "Tool id, e.g. MasonTile.PaintRect.")] string tool,
            [CliArg("args", "Arguments as a JSON object keyed by parameter name.")] string args = "{}")
        {
            if (string.IsNullOrEmpty(BenchScene.ActiveTask))
                return new ToolCallOutcome { Tool = tool, Ok = false, Error = "No bench task is set up.", Response = "No bench task is set up." };

            JObject parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(args) ? new JObject() : JObject.Parse(args);
            }
            catch (Newtonsoft.Json.JsonException e)
            {
                return new ToolCallOutcome { Tool = tool, Ok = false, Error = "args is not a JSON object: " + e.Message, Response = "args is not a JSON object: " + e.Message };
            }
            return ToolInvoker.Invoke(tool, parsed);
        }

        [CliCommand(Group, "note", "Show a message from the agent in the Agent Bench window, between its tool calls.")]
        static string Note([CliArg("text", "What the agent said.")] string text)
        {
            BenchMonitor.Note(text);
            return "ok";
        }

        [CliCommand(Group, "monitor", "Open the Agent Bench window and report whether it is replaying. Optionally start a paced replay.")]
        static object Monitor(
            [CliArg("replay", "Start a paced replay: 'all', a suite name, or a qualified task id. Empty: just report.")] string replay = "",
            [CliArg("pace", "Seconds between the replayed tool calls.")] float pace = 0.45f,
            [CliArg("layout", "'right': float the window over the right third of the main window. Empty: leave it where it is.")] string layout = "",
            [CliArg("clear", "Clear the window's task list first.")] bool clear = false)
        {
            Rect? rect = null;
            if (layout == "right")
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                var width = Mathf.Round(main.width * 0.3f);
                rect = new Rect(main.xMax - width - 4f, main.y + 60f, width, main.height - 90f);
            }
            var window = AgentBenchWindow.Open(rect);
            if (clear) window.ClearHistory();
            if (!string.IsNullOrEmpty(replay)) window.StartReplay(MatchTasks(replay), pace);
            return new { replaying = window.IsReplaying, activeTask = BenchScene.ActiveTask };
        }

        [CliCommand(Group, "check", "Judge the open bench scene against the task's expectations.")]
        static CheckResult Check([CliArg("task", "Qualified task id.")] string task) => BenchRunner.Check(task);

        [CliCommand(Group, "teardown", "Close the bench scene and reopen the scenes that were open before setup.")]
        static string Teardown()
        {
            var active = BenchScene.ActiveTask;
            BenchRunner.Teardown();
            return string.IsNullOrEmpty(active) ? "Nothing to tear down." : $"Closed '{active}'.";
        }

        [CliCommand(Group, "replay", "Tier 1: run reference solutions and check them. 'all', a suite name, or a qualified task id.")]
        static object Replay([CliArg("task", "'all', a suite name, or a qualified task id.")] string task = "all")
        {
            var reports = MatchTasks(task).Select(id => BenchRunner.Replay(id)).ToList();
            return new
            {
                passed = reports.Count(r => r.Passed),
                total = reports.Count,
                tasks = reports.Select(r => new
                {
                    task = r.Task,
                    passed = r.Passed,
                    before = r.Before?.Score,
                    after = r.After?.Score,
                    calls = r.Calls.Count,
                    responseChars = r.ResponseChars,
                    failures = (r.After?.Items ?? new List<CheckItem>()).Where(i => !i.Passed).Select(i => $"{i.Expectation}: {i.Detail}")
                        .Concat(r.Calls.Where(c => !c.Ok).Select(c => $"{c.Tool} failed: {c.Error}"))
                        .Concat(r.Before != null && r.Before.Passed ? new[] { "checker already passes on the untouched setup" } : new string[0])
                        .ToList(),
                }).ToList(),
            };
        }

        static List<string> MatchTasks(string task)
        {
            var ids = new List<string>();
            foreach (var suite in BenchSuite.All())
            foreach (var t in suite.CreateTasks())
            {
                var id = suite.QualifiedId(t);
                if (task == "all" || task == suite.Name || task == id) ids.Add(id);
            }
            if (ids.Count == 0) throw new System.ArgumentException($"No tasks match '{task}'.");
            return ids;
        }
    }
}
