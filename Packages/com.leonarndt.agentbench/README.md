# Unity Agent Bench

**Measure how well an agent gets creator tasks done with your Unity AI Assistant tools, and catch regressions when you change them.**

A tool package such as [MasonTile](../com.leonarndt.masontile/README.md) is only as good as the work an agent gets
done with it. Unit tests check each tool on its own. They don't tell you whether a reworded description, a new
parameter or a smaller snapshot helps an agent build a room or makes it get lost. The bench answers that by
running creator tasks two ways:

| | Tier 1: replay | Tier 2: agent run |
|---|---|---|
| Who solves the task | the task's reference solution, as fixed tool calls | Claude Code, headless, given only the suite's tools |
| Needs a model | no | yes (your Claude Code login) |
| Time | ~2 s for the whole suite | ~10–60 s per trial |
| Catches | broken tools, coordinate bugs, checkers that accept anything | tool descriptions and results that confuse an agent, missing tools, wasted calls and tokens |
| Run it | in every test run (EditMode tests) | before merging a change to the tools; compare against a baseline |

Both tiers use the same tasks and the same checkers, and the tools run exactly as AI Assistant calls them.

## How it works

```
agentbench.py run ──setup──▶ Pipeline server ──▶ BenchRunner: empty scene + task setup
        │
        └─ claude -p "<task prompt>"  (only the suite's tools, empty cwd, no built-ins)
                 │ MCP (stdio)
                 ▼
           mcp_server.py ──agentbench call──▶ ToolInvoker ──▶ [AgentTool] method
        ┌──────────────────────────────────────────────────────────┘
agentbench.py ──check──▶ task checker ──teardown──▶ your scenes reopened
```

- **Tasks** (`BenchTask`) are creator requests such as *"Add a 4x2 water pool exactly in the middle of the room"*.
  Each has a setup, a checker, and a reference solution.
- **Checkers judge the result, not the route.** A checker reads the scene and returns named expectations
  (`the gap in the wall is closed`, `nothing leaked outside the enclosure`), so any correct solution passes and a
  failure says what is wrong. `Score` is the share of expectations met, for partial credit.
- **Tool schemas come from the real attributes.** `ToolCatalog` builds each JSON schema from `[AgentTool]` and
  `[ToolParameter]`, including C# defaults. Change a description and the agent sees the change, and `compare`
  lists which tool definitions differ between two runs.
- **Tools run as AI Assistant runs them.** `ToolInvoker` binds JSON arguments by name, injects a
  `ToolExecutionContext`, grants permissions (and counts the checks), and serialises results and errors the way the
  Assistant does. Unknown or missing parameters come back as errors, as a model would see them.
- **Your scene is safe.** Each task runs in a fresh unsaved scene, and teardown reopens the scenes you had open.
  Setup refuses to start while you have unsaved changes. Nothing is written to the project.
- **The agent is isolated.** Claude Code runs from an empty folder with built-in tools off (`--tools ""`), only the
  bench's MCP server (`--strict-mcp-config`), the suite's system prompt, and no user settings, hooks or plugins.

## Requirements

- `com.unity.ai.assistant` 2.x (the tools under test) and `com.unity.pipeline` (Editor access from the harness).
- An open Editor with the project loaded. The harness talks to it on `127.0.0.1` with the Pipeline bearer token.
- Tier 2: Python 3.10+ (standard library only) and Claude Code. The harness finds `claude` on `PATH`, in
  `$AGENTBENCH_CLAUDE`, or in the VS Code extension.

## Usage

Run from anywhere inside the Unity project:

```bash
H=Packages/com.leonarndt.agentbench/Harness~/agentbench.py

python3 $H list --prompts                     # suites, tasks, tags, prompts
python3 $H replay                             # tier 1, exit code 1 on any failure
python3 $H run --trials 3                     # tier 2, all tasks, Sonnet
python3 $H run --task corridor --task center-pool --model opus --trials 5
python3 $H run --tag read-first --label my-change
python3 $H report AgentBenchRuns/<run>
python3 $H compare AgentBenchRuns/<baseline> AgentBenchRuns/<candidate> --fail-on-regression
```

Tier 1 also runs as EditMode tests (`AgentBench.Tests.Editor`), one test per task plus a check that every tool
parameter has a description:

```bash
unity command test run --mode editor --filter AgentBench.Tests.Editor --filter-type assembly
```

The Pipeline commands behind the harness can also be called directly, e.g. to step through a task by hand:

```bash
unity command agentbench setup --task masontile/seal-and-flood
unity command agentbench call --tool MasonTile.FloodFill --args '{"tilemap":"Ground","x":1,"y":1,"tile":"water"}'
unity command agentbench check --task masontile/seal-and-flood
unity command agentbench teardown
```

A run reopens your scenes once, at the end: each `setup` replaces the previous task's scene.

### Watching a run

**Window > Agent Bench** shows the bench as it runs:

- the task, its prompt and who is solving it (`Claude Code · sonnet`, or `Reference solution`);
- every tool call with its arguments and the start of its response, and what the agent says between calls;
- the checker's verdict, expectation by expectation, and a tally of the tasks so far.

With **Follow in Scene view** on, the Scene view frames what the agent builds and zooms out as it grows. Your
Scene view camera is put back when the bench scene closes. **Replay** plays reference solutions slowly, one call at
a time, to see what each task asks for.

Add `--hold 3` to `run` to keep each result on screen for 3 seconds before the next task (the time doesn't count
towards the trial's `seconds`). The same window can be driven over the Pipeline server:

```bash
unity command agentbench monitor --layout right                    # open it, floating over the right third
unity command agentbench monitor --replay masontile --pace 0.5     # slow replay of a suite
```

`Assets/Showcase/Tools~/record-agentbench.py` in this repo uses this to record a demo video.

### Verifying a change to the tools

1. On `main`, run `agentbench.py run --trials 3 --label baseline`. Keep that run folder.
2. Make the change. `agentbench.py replay` must still pass.
3. Run `agentbench.py run --trials 3 --label my-change`.
4. Run `agentbench.py compare AgentBenchRuns/*-baseline AgentBenchRuns/*-my-change`.

`compare` reports, per task, the pass rate, tool calls, tool errors and input tokens before and after. It flags a
task as a regression when its pass rate drops by at least one trial in three. Agents are not deterministic, so use 3
or more trials per task, and treat a single-trial difference as noise until you have rerun it.

### Output

Each run writes `AgentBenchRuns/<time>-<label>/` (git-ignored):

- `results.json`: run metadata (model, Claude Code version, git commit and whether `Packages/` had uncommitted
  changes, a fingerprint of the tool definitions), the exact tool definitions, and one record per trial: passed,
  score, failed expectations, tool calls, tool errors, response characters, turns, tokens, cost, seconds, the
  agent's final message.
- Per trial: `transcript.jsonl` (Claude Code stream-json), `calls.jsonl` (every tool call with arguments, result
  size and time), `stderr.txt`.

A trial the harness could not run properly (the Editor was unreachable, the MCP server didn't connect, Claude Code
gave no result) has the status `error` and is left out of pass rates, so infrastructure problems don't read as
agent failures.

## Adding a suite

Derive from `BenchSuite` in an Editor assembly. Suites are found automatically, and every task gets a tier 1 test.

```csharp
public sealed class MySuite : BenchSuite
{
    public override string Name => "mytools";
    public override string ToolIdPrefix => "MyTools.";   // tools offered to the agent

    public override IEnumerable<BenchTask> CreateTasks()
    {
        yield return new BenchTask("make-red-cube",
            "Create a red cube named 'Box' at the origin.",
            setup: () => { },
            check: () => new Checks()
                .Expect(GameObject.Find("Box") != null, "a GameObject named Box exists")
                .Result(),
            new ToolCall("MyTools.CreatePrimitive", new { type = "Cube", name = "Box", color = "red" }));
    }
}
```

Rules for good tasks:

- **The untouched setup must fail the checker.** Tier 1 enforces this. Otherwise the task tests nothing.
- **Checkers must not use memory from setup.** Setup and check run in separate requests, so recompute what you
  expect from constants, or read it back from the scene.
- **Write the prompt as a creator would.** Leave out positions the agent can look up (`read-first`), and don't
  name the tools to use.
- **Tag what the task exercises**, so you can slice results with `--tag`. The MasonTile suite uses `basic`,
  `read-first`, `feedback`, `coordinates`, `multi-tilemap`, `generation`, `planning`, `tool-gap`, `large-map`
  and `spatial`.
- A reference solution is enumerated lazily, so later calls can depend on earlier results (see
  `cave-with-coins`, which places coins on cells the generated cave left open).

## Limitations

- The agent is Claude Code with a bench system prompt, not AI Assistant itself. The tools, schemas, permissions
  and result format match, but the Assistant's own system prompt, model routing and context are not reproduced.
  Tool names are `MasonTile_PaintRect` rather than `MasonTile.PaintRect`, because MCP names can't contain `.`.
- Tools must complete synchronously apart from permission checks. A tool that awaits anything else (a user
  interaction, a download) is reported as an error rather than blocking the Editor.
- Trials run one at a time against one Editor.
