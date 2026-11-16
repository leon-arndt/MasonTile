# Project rules

This repository holds two Unity packages, `Packages/com.leonarndt.masontile` and `Packages/com.leonarndt.agentbench`.
They are consumed by git URL with per-package tags (`masontile-vX.Y.Z`, `agentbench-vX.Y.Z`).

## Releasing

- Bump `version` in the package's `package.json`, move the changelog's Unreleased section under the new version, and
  update the install URLs in the READMEs.
- Tag the release commit `masontile-vX.Y.Z` or `agentbench-vX.Y.Z`. Don't move a published tag; fix forward.
- Every file inside a package needs a committed `.meta` file. Packages installed from git are read-only, so Unity
  can't generate missing ones.

## No mutable static fields

The packages must work with Enter Play Mode set to **Do not reload Domain or Scene**, which is how Unity runs under
CoreCLR. Static fields are not reset when Play Mode starts, so mutable static state carries over between Play sessions
and causes bugs that only show up on the second run.

- Do not add mutable static fields, static auto-properties with setters, `static Instance` singletons or static
  events.
- `const` is allowed. So is `static readonly` holding immutable data that nothing writes to after it is
  initialised. Pure static methods are allowed too.
- Keep state on a MonoBehaviour or ScriptableObject instance, or pass it in as a parameter.
- On Unity 6.7 and newer, the analyzer (warnings UAL0010 and UAL0013) requires every type with static fields to say how
  they are cleaned up, using an attribute from `Unity.Scripting.LifecycleManagement`. Treat these warnings as errors.
  The packages support Unity 6000.4, so wrap the `using` and the attribute in `#if UNITY_6000_7_OR_NEWER`.
  - `[NoAutoStaticsCleanup]`: the statics are read-only data, or the type resets them by hand. Add a short comment
    saying which.
  - `[AutoStaticsCleanup]`: Unity resets the statics to their defaults on its own. Use this only when that reset is
    all that is needed.
- If static state cannot be avoided, reset it explicitly and say why in a comment:
  - Runtime: reset it in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
    method.
  - Editor: clear it, and remove any `EditorApplication.update` subscriptions, on
    `EditorApplication.playModeStateChanged` (`ExitingEditMode`) as well as on
    `AssemblyReloadEvents.beforeAssemblyReload`.
- Test changes by entering and exiting Play Mode at least twice in a row.
