// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Properties every GitHub Actions workflow of this repository keeps, checked on each file so that
// the next workflow added, or the next edit of one, cannot quietly drop them:
//
// - a top-level `permissions` block that grants reading the repository and nothing more. Without
//   one the job token gets the repository's default, which may include writing to it, and any
//   step -- a dependency's install script among them -- can use it;
// - every action pinned to a full commit, so a moved tag cannot change what runs;
// - the integration workflow's shard step stops when the planner fails or chooses no classes,
//   rather than writing an empty filter, which `dotnet test` reads as "every class". That step is
//   run here, with a stand-in for the planner, rather than read.
//
// The workflows are read as text: the repository has no YAML parser among its dependencies, and
// the shapes checked here are plain enough not to need one.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const workflowDir = join(repoRoot, '.github', 'workflows');
const workflows = readdirSync(workflowDir)
  .filter((name) => /\.ya?ml$/.test(name))
  .map((name) => ({ name, text: readFileSync(join(workflowDir, name), 'utf8') }));

/** Lines without their comments, so a commented-out line can neither satisfy nor break a check. */
const codeLines = (text) =>
  text
    .split('\n')
    .map((line) => (line.trimStart().startsWith('#') ? '' : line.replace(/\s+#.*$/, '')));

/** The entries of the top-level `permissions:` block, or undefined when there is none. */
function topLevelPermissions(text) {
  const lines = codeLines(text);
  const at = lines.findIndex((line) => /^permissions:/.test(line));
  if (at < 0) return undefined;
  const inline = lines[at].slice('permissions:'.length).trim();
  if (inline) return { '*': inline }; // `permissions: read-all` or `write-all`
  const entries = {};
  for (const line of lines.slice(at + 1)) {
    if (line.trim() === '') continue;
    if (!/^\s/.test(line)) break;
    const m = /^\s+([\w-]+):\s*(\S+)\s*$/.exec(line);
    if (m) entries[m[1]] = m[2];
  }
  return entries;
}

/** Every action a workflow uses that is not pinned to a 40-hex commit. */
function unpinnedActions(text) {
  return codeLines(text)
    .map((line) => /^\s*(?:-\s+)?uses:\s*["']?([^"'\s]+)/.exec(line)?.[1])
    .filter((ref) => ref && !ref.startsWith('./') && !ref.startsWith('docker://') && !/@[0-9a-f]{40}$/.test(ref));
}

/** The `run:` script of the step with this name, unindented. */
function stepScript(text, stepName) {
  const lines = text.split('\n');
  const at = lines.findIndex((line) => line.trim() === `- name: ${stepName}`);
  if (at < 0) return undefined;
  const stepIndent = lines[at].indexOf('-');
  for (let i = at + 1; i < lines.length; i++) {
    const line = lines[i];
    if (line.trim() !== '' && line.search(/\S/) <= stepIndent) return undefined; // next step
    if (/^\s+run:\s*\|\s*$/.test(line)) {
      const body = [];
      let indent;
      for (const next of lines.slice(i + 1)) {
        if (next.trim() === '') {
          body.push('');
          continue;
        }
        const own = next.search(/\S/);
        indent ??= own;
        if (own < indent) break;
        body.push(next.slice(indent));
      }
      return body.join('\n');
    }
  }
  return undefined;
}

describe('every workflow', () => {
  it('there are workflows to check', () => assert.ok(workflows.length >= 1));

  for (const { name, text } of workflows) {
    it(`${name} grants its jobs no more than reading the repository`, () => {
      const permissions = topLevelPermissions(text);
      assert.ok(permissions, `${name} has no top-level permissions block`);
      assert.equal(permissions.contents, 'read', `${name} grants contents: read`);
      for (const [scope, level] of Object.entries(permissions)) {
        assert.ok(['read', 'none'].includes(level), `${name} grants ${scope}: ${level} to every job`);
      }
    });

    it(`${name} pins every action to a commit`, () => {
      assert.deepEqual(unpinnedActions(text), [], `${name} uses actions by a tag or branch that can move`);
    });
  }
});

describe('the checks above, on the shapes they must refuse', () => {
  it('refuses a workflow with no permissions, write access or read-all', () => {
    assert.equal(topLevelPermissions('on: push\njobs:\n  a:\n    permissions:\n      contents: read\n'), undefined);
    assert.deepEqual(topLevelPermissions('permissions:\n  contents: write\n  # issues: write\njobs: {}\n'), { contents: 'write' });
    assert.deepEqual(topLevelPermissions('permissions: write-all\n'), { '*': 'write-all' });
  });

  it('refuses an action by tag or branch', () => {
    const text = [
      '      - uses: actions/checkout@v4',
      '        uses: actions/upload-artifact@main # pinned later',
      '      - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020 # v4.4.0',
      '      - uses: ./.github/actions/local',
      '      # - uses: actions/cache@v4',
    ].join('\n');
    assert.deepEqual(unpinnedActions(text), ['actions/checkout@v4', 'actions/upload-artifact@main']);
  });
});

describe("the integration workflow's shard step", { skip: process.platform === 'win32' && 'the step is a bash script' }, () => {
  const integration = workflows.find((w) => w.name === 'integration.yml')?.text ?? '';
  const script = stepScript(integration, "Choose this job's classes");

  /** Runs the step as the runner does (`bash -e`), with `node` standing in for the planner. */
  function runStep({ planner, filterInput = '' }) {
    const root = mkdtempSync(join(tmpdir(), 'silexgis-shard-step-'));
    try {
      const bin = join(root, 'bin');
      mkdirSync(bin);
      writeFileSync(join(bin, 'node'), `#!/usr/bin/env bash\n${planner}\n`);
      chmodSync(join(bin, 'node'), 0o755);
      const output = join(root, 'github-output');
      writeFileSync(output, '');
      const body = script
        .replaceAll('${{ matrix.index }}', '2')
        .replaceAll('${{ needs.plan.outputs.count }}', '4');
      const r = spawnSync('bash', ['--noprofile', '--norc', '-eo', 'pipefail', '-c', body], {
        encoding: 'utf8',
        env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, GITHUB_OUTPUT: output, FILTER: filterInput },
      });
      return { status: r.status, stdout: r.stdout, stderr: r.stderr, output: readFileSync(output, 'utf8') };
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  }

  it('is there to run', () => {
    assert.ok(script, "integration.yml has a step named \"Choose this job's classes\" with a run: | script");
    assert.doesNotMatch(script, /\$\{\{ (?!matrix\.index|needs\.plan\.outputs\.count)/, 'every expression it uses is supplied here');
  });

  it("writes the planner's filter", () => {
    const r = runStep({ planner: 'case "$*" in *--list*) echo "listing";; *) echo "FullyQualifiedName~ShardTwo";; esac' });
    assert.equal(r.status, 0, r.stderr);
    assert.equal(r.output, 'filter=FullyQualifiedName~ShardTwo\n');
  });

  it('passes a filter given by hand through untouched', () => {
    const r = runStep({ planner: 'exit 1', filterInput: 'FullyQualifiedName~Chosen' });
    assert.equal(r.status, 0, r.stderr);
    assert.equal(r.output, 'filter=FullyQualifiedName~Chosen\n');
  });

  it('fails, writing no filter, when the planner fails', () => {
    const r = runStep({ planner: 'echo "planner broke" >&2; exit 3' });
    assert.notEqual(r.status, 0);
    assert.doesNotMatch(r.output, /filter=/);
  });

  it('fails, writing no filter, when the planner chooses nothing', () => {
    const r = runStep({ planner: 'exit 0' });
    assert.notEqual(r.status, 0);
    assert.doesNotMatch(r.output, /filter=/);
  });
});
