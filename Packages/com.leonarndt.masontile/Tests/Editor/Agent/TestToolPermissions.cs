using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using Object = UnityEngine.Object;

namespace MasonTile.Tests.Agent
{
    /// <summary>
    /// Stands in for the AI Assistant's permission system: records every Unity object check and
    /// either allows it or rejects it the way a user clicking "Deny" would.
    /// </summary>
    sealed class TestToolPermissions : IToolPermissions
    {
        public readonly struct ObjectCheck
        {
            public readonly PermissionItemOperation Operation;
            public readonly Type Type;
            public readonly Object Target;

            public ObjectCheck(PermissionItemOperation operation, Type type, Object target)
            {
                Operation = operation;
                Type = type;
                Target = target;
            }
        }

        public bool Deny;
        public readonly List<ObjectCheck> ObjectChecks = new();

        public Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, PermissionItemOperation operation, Type type, Object target,
            CancellationToken cancellationToken = default)
        {
            ObjectChecks.Add(new ObjectCheck(operation, type, target));
            return Deny ? Task.FromException(new UnauthorizedAccessException("Denied by test.")) : Task.CompletedTask;
        }

        // MasonTile tools only ask for Unity object access; anything else is a test failure.
        public Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Unexpected();
        public Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, PermissionItemOperation operation, string path, CancellationToken cancellationToken = default) => Unexpected();
        public Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code, CancellationToken cancellationToken = default) => Unexpected();
        public Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Unexpected();
        public Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, PermissionPlayModeOperation operation, CancellationToken cancellationToken = default) => Unexpected();
        public Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default) => Unexpected();

        public void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, Object target) { }
        public void ResetTemporaryPermissions() { }
        public void ResetIgnoredObjects() { }
        public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> permissions) { }

        static Task Unexpected() => Task.FromException(new InvalidOperationException("MasonTile tools should only request Unity object access."));
    }
}
