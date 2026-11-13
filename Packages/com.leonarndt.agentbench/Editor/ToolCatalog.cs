using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace AgentBench
{
    /// <summary>An <c>[AgentTool]</c> method and the JSON schema an agent sees for it.</summary>
    public sealed class ToolInfo
    {
        public string Id { get; }
        public string Description { get; }
        public MethodInfo Method { get; }

        /// <summary>JSON Schema of the arguments, built from the <c>[ToolParameter]</c> descriptions and C# defaults.</summary>
        public JObject InputSchema { get; }

        internal ToolInfo(string id, string description, MethodInfo method, JObject inputSchema)
        {
            Id = id;
            Description = description;
            Method = method;
            InputSchema = inputSchema;
        }
    }

    /// <summary>
    /// Finds AI Assistant tools by reflection and describes them as JSON Schema. Reads the same attributes
    /// the Assistant does, so editing a tool's description or parameters changes what the benchmarked
    /// agent sees, which is the point: description changes are measurable.
    /// </summary>
    public static class ToolCatalog
    {
        /// <summary>Tools whose id starts with <paramref name="idPrefix"/>, ordered by id.</summary>
        public static List<ToolInfo> Find(string idPrefix)
        {
            return TypeCache.GetMethodsWithAttribute<AgentToolAttribute>()
                .Where(m => m.IsStatic)
                .Select(m => (method: m, attr: m.GetCustomAttribute<AgentToolAttribute>()))
                .Where(x => x.attr.Id != null && x.attr.Id.StartsWith(idPrefix ?? "", StringComparison.Ordinal))
                .OrderBy(x => x.attr.Id, StringComparer.Ordinal)
                .Select(x => new ToolInfo(x.attr.Id, x.attr.Description, x.method, BuildSchema(x.method)))
                .ToList();
        }

        public static bool TryGet(string id, out ToolInfo tool)
        {
            tool = Find(id).FirstOrDefault(t => t.Id == id);
            return tool != null;
        }

        /// <summary>Parameters the agent fills in: everything except the injected <see cref="ToolExecutionContext"/>.</summary>
        public static IEnumerable<ParameterInfo> AgentParameters(MethodInfo method) =>
            method.GetParameters().Where(p => p.ParameterType != typeof(ToolExecutionContext));

        static JObject BuildSchema(MethodInfo method)
        {
            var properties = new JObject();
            var required = new JArray();
            foreach (var p in AgentParameters(method))
            {
                var schema = TypeSchema(p.ParameterType);
                var description = p.GetCustomAttribute<ToolParameterAttribute>()?.Description;
                if (!string.IsNullOrEmpty(description)) schema["description"] = description;
                if (p.HasDefaultValue)
                {
                    if (p.DefaultValue != null) schema["default"] = JToken.FromObject(p.DefaultValue);
                }
                else
                {
                    required.Add(p.Name);
                }
                properties[p.Name] = schema;
            }

            var result = new JObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0) result["required"] = required;
            return result;
        }

        static JObject TypeSchema(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(string)) return new JObject { ["type"] = "string" };
            if (t == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return new JObject { ["type"] = "integer" };
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return new JObject { ["type"] = "number" };
            if (t.IsEnum) return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(t)) };

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && t.GetGenericArguments()[0] == typeof(string))
                return new JObject { ["type"] = "object", ["additionalProperties"] = TypeSchema(t.GetGenericArguments()[1]) };

            var element = t.IsArray ? t.GetElementType()
                : t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t) ? t.GetGenericArguments()[0]
                : null;
            if (element != null) return new JObject { ["type"] = "array", ["items"] = TypeSchema(element) };

            return new JObject { ["type"] = "object" };
        }
    }
}
