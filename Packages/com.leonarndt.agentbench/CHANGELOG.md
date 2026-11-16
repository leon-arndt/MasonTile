# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-16

First release, from https://github.com/leon-arndt/MasonTile under the MIT license (tag `agentbench-v0.1.0`).

### Added
- `BenchSuite`, `BenchTask` and `Checks`: creator tasks with a setup, a checker that returns named expectations,
  and a reference solution. Suites are discovered automatically.
- Tier 1: every task's reference solution replays as an EditMode test (`AgentBench.Tests.Editor`), which also
  checks that the untouched setup fails the checker and that every tool parameter has a description.
- Tier 2: `Harness~/agentbench.py` runs Claude Code headless against each task through the Pipeline server and an
  MCP bridge, with `list`, `replay`, `run`, `report` and `compare` commands. Runs record tool calls, errors, tokens,
  cost and transcripts.
- `ToolCatalog` and `ToolInvoker`: JSON schemas built from the real `[AgentTool]` attributes, and tool calls made
  the way Unity AI Assistant makes them, including permission checks.
- Window > Agent Bench: live view of a run, with Scene view follow and slow replay of reference solutions.
- `agentbench` Pipeline commands: `setup`, `call`, `check`, `teardown` and `monitor`.
