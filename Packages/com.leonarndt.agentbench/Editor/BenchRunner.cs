using System;
using System.Collections.Generic;
using System.Linq;

namespace AgentBench
{
    /// <summary>What a reference replay did, for tier 1 and for comparing the tools' response sizes between versions.</summary>
    [Serializable]
    public sealed class ReplayReport
    {
        public string Task;

        /// <summary>The checker on the untouched setup. It must fail, or the task tests nothing.</summary>
        public CheckResult Before;

        public CheckResult After;
        public List<ToolCallOutcome> Calls = new();

        /// <summary>Total characters of tool responses: a proxy for the context the tools cost an agent.</summary>
        public int ResponseChars;

        public bool Passed => After != null && After.Passed && Before != null && !Before.Passed && Calls.All(c => c.Ok);
    }

    /// <summary>The task lifecycle: set up a bench scene, check it, tear it down; and replaying reference solutions.</summary>
    public static class BenchRunner
    {
        /// <summary>The agent name shown while a reference solution plays.</summary>
        public const string ReferenceAgent = "Reference solution";

        /// <summary>
        /// Opens a fresh scene and builds the task's starting state in it. If another task is still set up, its
        /// suite is cleaned up first and the user's scenes stay saved for the final <see cref="Teardown"/>, so a
        /// run of several tasks doesn't flash the user's scene between them.
        /// </summary>
        /// <param name="agent">Who will solve the task, shown in the Agent Bench window (e.g. "Claude Code · sonnet").</param>
        public static (BenchSuite suite, BenchTask task) Setup(string qualifiedTaskId, bool discardUnsaved = false, string agent = null)
        {
            var (suite, task) = BenchSuite.FindTask(qualifiedTaskId);
            CleanupActiveSuite();
            BenchScene.Open(qualifiedTaskId, discardUnsaved);
            try
            {
                task.Setup();
            }
            catch
            {
                Teardown();
                throw;
            }
            BenchMonitor.TaskStarted(qualifiedTaskId, task, agent);
            return (suite, task);
        }

        /// <summary>Judges the open bench scene. Fails if a different task's scene is open.</summary>
        public static CheckResult Check(string qualifiedTaskId)
        {
            var active = BenchScene.ActiveTask;
            if (active != qualifiedTaskId)
                throw new InvalidOperationException(string.IsNullOrEmpty(active)
                    ? $"No bench task is set up; run setup for '{qualifiedTaskId}' first."
                    : $"The open bench scene belongs to '{active}', not '{qualifiedTaskId}'.");
            var result = BenchSuite.FindTask(qualifiedTaskId).task.Check();
            BenchMonitor.Checked(qualifiedTaskId, result);
            return result;
        }

        /// <summary>Cleans up the active task and reopens the user's scenes. Safe to call when nothing is set up.</summary>
        public static void Teardown()
        {
            if (string.IsNullOrEmpty(BenchScene.ActiveTask)) return;
            try
            {
                CleanupActiveSuite();
            }
            finally
            {
                BenchScene.Close();
                BenchMonitor.TaskEnded();
            }
        }

        /// <summary>Lets the active task's suite free what its setup created. The bench scene itself stays open.</summary>
        static void CleanupActiveSuite()
        {
            var active = BenchScene.ActiveTask;
            if (string.IsNullOrEmpty(active)) return;
            BenchSuite suite;
            try
            {
                suite = BenchSuite.FindTask(active).suite;
            }
            catch (ArgumentException)
            {
                return; // the task was renamed or removed since setup; nothing of it is left to clean up
            }
            suite.Cleanup();
        }

        /// <summary>Sets up the task, runs its reference solution through <see cref="ToolInvoker"/>, checks, and tears down.</summary>
        public static ReplayReport Replay(string qualifiedTaskId, bool discardUnsaved = false)
        {
            var report = new ReplayReport { Task = qualifiedTaskId };
            var (_, task) = Setup(qualifiedTaskId, discardUnsaved, ReferenceAgent);
            try
            {
                report.Before = task.Check();
                foreach (var call in task.Reference())
                {
                    var outcome = ToolInvoker.Invoke(call);
                    report.Calls.Add(outcome);
                    if (!outcome.Ok) break;
                }
                report.After = task.Check();
                BenchMonitor.Checked(qualifiedTaskId, report.After);
                report.ResponseChars = report.Calls.Sum(c => c.Response?.Length ?? 0);
            }
            finally
            {
                Teardown();
            }
            return report;
        }
    }
}
