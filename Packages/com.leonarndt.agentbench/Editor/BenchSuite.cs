using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace AgentBench
{
    /// <summary>
    /// A set of tasks for one tool package. Derive from this in an Editor assembly; suites are found
    /// automatically, and every task gets tier 1 coverage (see <c>ReferenceSolutionTests</c>) without
    /// writing a test.
    /// </summary>
    public abstract class BenchSuite
    {
        /// <summary>Lowercase name used in task ids (<c>suite/task</c>) and on the command line.</summary>
        public abstract string Name { get; }

        /// <summary>Tools whose id starts with this are the ones offered to the agent, e.g. <c>MasonTile.</c>.</summary>
        public abstract string ToolIdPrefix { get; }

        /// <summary>
        /// System prompt for the agent. Keep it close to how the tools are used in AI Assistant: the bench
        /// should measure the tools, not prompt engineering.
        /// </summary>
        public virtual string SystemPrompt =>
            "You are an assistant inside the Unity Editor. You operate the open scene only through the tools you are given; " +
            "you cannot see the screen. Complete the user's request, check your work with the tools, then reply with one " +
            "short sentence saying what you did.";

        /// <summary>The tasks, created fresh on every call so no state is shared between runs.</summary>
        public abstract IEnumerable<BenchTask> CreateTasks();

        /// <summary>
        /// Destroys anything the tasks created outside the scene (in-memory assets such as tiles), before the
        /// bench scene is closed. Scene objects go away with the scene.
        /// </summary>
        public virtual void Cleanup() { }

        public string QualifiedId(BenchTask task) => $"{Name}/{task.Id}";

        /// <summary>All suites in loaded assemblies, ordered by name.</summary>
        public static List<BenchSuite> All() =>
            TypeCache.GetTypesDerivedFrom<BenchSuite>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (BenchSuite)Activator.CreateInstance(t))
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ToList();

        public static BenchSuite Find(string name)
        {
            var all = All();
            return all.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?? throw new ArgumentException($"No bench suite named '{name}'. Available: {string.Join(", ", all.Select(s => s.Name))}.");
        }

        /// <summary>Resolves a qualified task id (<c>suite/task</c>).</summary>
        public static (BenchSuite suite, BenchTask task) FindTask(string qualifiedId)
        {
            var slash = qualifiedId?.IndexOf('/') ?? -1;
            if (slash <= 0) throw new ArgumentException($"Task ids look like 'suite/task', got '{qualifiedId}'.");

            var suite = Find(qualifiedId.Substring(0, slash));
            var taskId = qualifiedId.Substring(slash + 1);
            var tasks = suite.CreateTasks().ToList();
            var task = tasks.FirstOrDefault(t => t.Id == taskId)
                       ?? throw new ArgumentException($"Suite '{suite.Name}' has no task '{taskId}'. Tasks: {string.Join(", ", tasks.Select(t => t.Id))}.");
            return (suite, task);
        }
    }
}
