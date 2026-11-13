using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AgentBench
{
    /// <summary>
    /// Passes bench events to every open <see cref="AgentBenchWindow"/>. Holds no state of its own (see CLAUDE.md):
    /// the windows keep what they show, and with no window open every call is a cheap no-op.
    /// </summary>
    public static class BenchMonitor
    {
        static AgentBenchWindow[] Windows() => Resources.FindObjectsOfTypeAll<AgentBenchWindow>();

        internal static void TaskStarted(string qualifiedId, BenchTask task, string agent)
        {
            foreach (var w in Windows()) w.OnTaskStarted(qualifiedId, task.Prompt, task.Tags, agent);
        }

        internal static void ToolCalled(string toolId, JObject args, ToolCallOutcome outcome)
        {
            foreach (var w in Windows()) w.OnToolCalled(toolId, args, outcome);
        }

        /// <summary>Shows what the agent said between tool calls (the tier 2 harness forwards it).</summary>
        public static void Note(string text)
        {
            foreach (var w in Windows()) w.OnNote(text);
        }

        internal static void Checked(string qualifiedId, CheckResult result)
        {
            foreach (var w in Windows()) w.OnChecked(qualifiedId, result);
        }

        internal static void TaskEnded()
        {
            foreach (var w in Windows()) w.OnTaskEnded();
        }
    }
}
