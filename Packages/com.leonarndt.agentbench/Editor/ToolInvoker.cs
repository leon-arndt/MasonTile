using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Object = UnityEngine.Object;

namespace AgentBench
{
    /// <summary>What one tool call returned, in the form the agent receives it.</summary>
    [Serializable]
    public sealed class ToolCallOutcome
    {
        public string Tool;
        public bool Ok;

        /// <summary>The text the agent reads: the result as compact JSON, or the error message.</summary>
        public string Response;

        public string Error;
        public double Milliseconds;

        /// <summary>Unity objects the tool asked permission to modify. A user would see one prompt per check.</summary>
        public int PermissionChecks;
    }

    /// <summary>
    /// Calls an <c>[AgentTool]</c> the way AI Assistant does: arguments bound by name from JSON, a
    /// <see cref="ToolExecutionContext"/> injected, the result serialised with Newtonsoft (enums as names), and
    /// an exception turned into an error message (the inner exception's, as the Assistant does).
    /// Permissions are granted automatically and counted.
    /// </summary>
    public static class ToolInvoker
    {
        public static ToolCallOutcome Invoke(ToolCall call) => Invoke(call.ToolId, call.Args);

        public static ToolCallOutcome Invoke(string toolId, JObject args)
        {
            var outcome = new ToolCallOutcome { Tool = toolId };
            var watch = Stopwatch.StartNew();
            var permissions = new BenchToolPermissions();
            try
            {
                if (!ToolCatalog.TryGet(toolId, out var tool))
                    throw new ArgumentException($"Unknown tool '{toolId}'.");

                var argv = BindArguments(tool, args ?? new JObject(), permissions);
                var returned = tool.Method.Invoke(null, argv);
                var result = Unwrap(tool.Method, returned);

                var serializer = new JsonSerializer { Converters = { new StringEnumConverter() } };
                outcome.Ok = true;
                outcome.Response = result == null ? "null" : JToken.FromObject(result, serializer).ToString(Formatting.None);
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException or AggregateException && e.InnerException != null ? e.InnerException : e;
                outcome.Ok = false;
                outcome.Error = inner.InnerException?.Message ?? inner.Message;
                outcome.Response = outcome.Error;
            }

            outcome.Milliseconds = watch.Elapsed.TotalMilliseconds;
            outcome.PermissionChecks = permissions.ObjectChecks;
            BenchMonitor.ToolCalled(toolId, args, outcome);
            return outcome;
        }

        static object[] BindArguments(ToolInfo tool, JObject args, IToolPermissions permissions)
        {
            var parameters = tool.Method.GetParameters();
            var known = new HashSet<string>(ToolCatalog.AgentParameters(tool.Method).Select(p => p.Name));
            var unknown = args.Properties().Select(p => p.Name).Where(n => !known.Contains(n)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"Unknown parameter(s) {string.Join(", ", unknown.Select(n => $"'{n}'"))} for {tool.Id}. " +
                                            $"Parameters: {string.Join(", ", known)}.");

            var argv = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(ToolExecutionContext))
                {
                    var callInfo = new ToolExecutionContext.CallInfo(tool.Id, Guid.NewGuid(), args.ToString(Formatting.None));
                    argv[i] = new ToolExecutionContext(null, callInfo, new ToolCallPermissions(callInfo, permissions, CancellationToken.None), default);
                }
                else if (args.TryGetValue(p.Name, out var token) && token.Type != JTokenType.Null)
                {
                    try
                    {
                        argv[i] = token.ToObject(p.ParameterType);
                    }
                    catch (Exception e) when (e is JsonException or FormatException or InvalidCastException or ArgumentException)
                    {
                        throw new ArgumentException($"Parameter '{p.Name}' expects {p.ParameterType.Name}, got {token.ToString(Formatting.None)}.");
                    }
                }
                else if (p.HasDefaultValue)
                {
                    argv[i] = p.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Missing required parameter '{p.Name}' for {tool.Id}.");
                }
            }
            return argv;
        }

        /// <summary>
        /// Gets the value out of an async tool. The bench's permission provider answers synchronously, so a tool
        /// that only awaits permission checks has finished by now; blocking the main thread to wait for anything
        /// else would deadlock the Editor, so that is reported instead.
        /// </summary>
        static object Unwrap(MethodInfo method, object returned)
        {
            if (returned is not Task task) return returned;
            if (!task.IsCompleted)
                throw new InvalidOperationException($"{method.Name} did not complete synchronously; the bench can only run tools that await nothing but permission checks.");

            task.GetAwaiter().GetResult(); // rethrows the tool's exception
            var type = method.ReturnType;
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>)
                ? type.GetProperty(nameof(Task<object>.Result)).GetValue(task)
                : null;
        }

        /// <summary>Grants everything, like a user who ticked "always allow", and counts the object checks.</summary>
        sealed class BenchToolPermissions : IToolPermissions
        {
            public int ObjectChecks;

            public Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, PermissionItemOperation operation, Type type, Object target,
                CancellationToken cancellationToken = default)
            {
                ObjectChecks++;
                return Task.CompletedTask;
            }

            public Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, PermissionItemOperation operation, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, PermissionPlayModeOperation operation, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, Object target) { }
            public void ResetTemporaryPermissions() { }
            public void ResetIgnoredObjects() { }
            public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> permissions) { }
        }
    }
}
