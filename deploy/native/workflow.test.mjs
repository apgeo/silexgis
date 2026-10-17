// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The workflow that proves the installation without Docker on a fresh machine is only worth
// having while it runs what an operator runs. It cannot run here; this pins what it is made of.
//
// Run with the rest: `node --test "deploy/**/*.test.mjs"`.

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const repoRoot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = (...parts) => fs.readFileSync(path.join(repoRoot, ...parts), 'utf8');

describe('the workflow that keeps proving it', () => {
  const workflow = read('.github', 'workflows', 'native-install.yml');

  it('runs on demand, weekly, and when what it installs changes', () => {
    assert.match(workflow, /workflow_dispatch:/);
    assert.match(workflow, /schedule:\s*\n\s*- cron:/);
    for (const watched of ["'deploy/native/**'", "'server/**'", "'client/package*.json'"]) {
      assert.ok(workflow.includes(watched), `pushes touching ${watched} run it`);
    }
  });

  it('installs, checks, backs up and restores, with the API as a plain process', () => {
    assert.match(workflow, /deploy\/native\/install\.mjs\s[^-]*--service process/);
    for (const script of ['check.mjs', 'backup.mjs', 'restore.mjs']) {
      assert.ok(workflow.includes(`deploy/native/${script}`), `the workflow runs ${script}`);
    }
  });
});
