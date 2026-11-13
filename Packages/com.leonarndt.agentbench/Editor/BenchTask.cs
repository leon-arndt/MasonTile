using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentBench
{
    /// <summary>
    /// One creator request the agent has to fulfil, e.g. "add a water pool in the middle of the room".
    ///
    /// A task is judged on the state it leaves behind, never on which tools were called, so any correct
    /// solution passes. Each task also carries a reference solution: tier 1 replays it to prove the task is
    /// solvable with the current tools and that the checker accepts a correct answer.
    /// </summary>
    public sealed class BenchTask
    {
        /// <summary>Short kebab-case id, unique within its suite.</summary>
        public string Id { get; }

        /// <summary>The request exactly as the agent receives it.</summary>
        public string Prompt { get; }

        /// <summary>Builds the starting state inside the empty bench scene.</summary>
        public Action Setup { get; }

        /// <summary>Judges the current state. Must not depend on anything kept in memory since <see cref="Setup"/>.</summary>
        public Func<CheckResult> Check { get; }

        /// <summary>
        /// A correct solution as tool calls. It is enumerated lazily, one call at a time after the previous
        /// call ran, so a later call can depend on what an earlier one produced (e.g. a generated cave).
        /// </summary>
        public Func<IEnumerable<ToolCall>> Reference { get; }

        /// <summary>Free-form labels for slicing results, e.g. "read-first", "planning", "tool-gap".</summary>
        public string[] Tags { get; set; } = Array.Empty<string>();

        public BenchTask(string id, string prompt, Action setup, Func<CheckResult> check, Func<IEnumerable<ToolCall>> reference)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Check = check ?? throw new ArgumentNullException(nameof(check));
            Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        }

        public BenchTask(string id, string prompt, Action setup, Func<CheckResult> check, params ToolCall[] reference)
            : this(id, prompt, setup, check, () => reference) { }

        public override string ToString() => Id;
    }

    /// <summary>A tool call by AI Assistant tool id, with arguments by parameter name.</summary>
    public sealed class ToolCall
    {
        public string ToolId { get; }
        public JObject Args { get; }

        /// <param name="toolId">The <c>[AgentTool]</c> id, e.g. <c>MasonTile.PaintRect</c>.</param>
        /// <param name="args">An anonymous object or dictionary whose members are the tool's parameter names.</param>
        public ToolCall(string toolId, object args = null)
        {
            ToolId = toolId ?? throw new ArgumentNullException(nameof(toolId));
            Args = args switch
            {
                null => new JObject(),
                JObject j => j,
                _ => JObject.FromObject(args),
            };
        }

        public override string ToString() => $"{ToolId}({Args.ToString(Formatting.None)})";
    }

    /// <summary>One expectation a checker tested.</summary>
    [Serializable]
    public sealed class CheckItem
    {
        public string Expectation;
        public bool Passed;

        /// <summary>What was found instead, only set when the expectation failed.</summary>
        public string Detail;
    }

    /// <summary>
    /// The verdict on a task. <see cref="Passed"/> requires every expectation to hold; <see cref="Score"/>
    /// is the fraction that held, for partial credit when comparing runs.
    /// </summary>
    [Serializable]
    public sealed class CheckResult
    {
        public bool Passed;
        public float Score;
        public List<CheckItem> Items = new();

        public override string ToString()
        {
            var failed = Items.Where(i => !i.Passed).Select(i => $"FAIL {i.Expectation}: {i.Detail}");
            return $"{(Passed ? "PASS" : "FAIL")} {Score:P0}" + (Passed ? "" : "\n" + string.Join("\n", failed));
        }
    }

    /// <summary>Builder for a <see cref="CheckResult"/>: record each expectation, then call <see cref="Result"/>.</summary>
    public sealed class Checks
    {
        readonly List<CheckItem> m_Items = new();

        /// <param name="passed">Whether the expectation holds.</param>
        /// <param name="expectation">What should be true, phrased as a statement ("room border is wall").</param>
        /// <param name="detail">What was found instead. Only evaluated when the expectation fails.</param>
        public Checks Expect(bool passed, string expectation, Func<string> detail = null)
        {
            m_Items.Add(new CheckItem { Expectation = expectation, Passed = passed, Detail = passed ? null : detail?.Invoke() });
            return this;
        }

        public CheckResult Result()
        {
            var passedCount = m_Items.Count(i => i.Passed);
            return new CheckResult
            {
                Passed = m_Items.Count > 0 && passedCount == m_Items.Count,
                Score = m_Items.Count == 0 ? 0f : (float)passedCount / m_Items.Count,
                Items = m_Items.ToList(),
            };
        }
    }
}
