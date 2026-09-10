// SPDX-License-Identifier: AGPL-3.0-or-later

// Regenerate — or verify — the reference topology metrics the cave topology tests assert against.
//
// The published reference implementation of karst network metrics is a Python library. It is a
// developer and CI tool here and nothing more: it is never a compose service, never a runtime
// dependency, and never on a request path. What ships is its OUTPUT — one JSON golden file per
// reference network, committed next to the network itself — so the .NET suite proves the metrics
// against the reference arithmetic on a machine with no Python at all. A broken install degrades
// the gate, not the test suite, and that split is the whole point of committing the goldens.
//
// Usage, from the repository root:
//   node scripts/karstnet-oracle.mjs            regenerate the goldens in place, then inspect and commit
//   node scripts/karstnet-oracle.mjs --check    regenerate into a scratch directory and fail on any drift
//
// --check is the form a gate runs. It answers one question: do the committed goldens still say
// what the pinned reference implementation says. It never writes into the repository.
//
// Everything is pinned — the base image by digest, the interpreter by that image, and every
// Python package including the transitive ones by scripts/karstnet-oracle/requirements.txt,
// installed with --no-deps so nothing is re-resolved. A metric that quietly changes when a
// transitive dependency ships a new release is exactly the failure the pinning prevents.
//
// Containerised because the host deliberately carries no usable Python: the system interpreter
// has neither pip nor venv, and installing either needs root. The same reasoning that keeps the
// database and geospatial client tools in containers applies here.

import { spawnSync } from 'node:child_process';
import { cpSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(here, '..');

// Pinned by digest, not by tag: python:3.11-slim is a moving target and the interpreter version
// decides which package versions resolve. 3.11 rather than a newer interpreter because the
// stereonet plotting package the reference library imports at module load is unmaintained and
// resolves cleanly only there — nothing here draws a stereonet, but the import is unconditional.
const IMAGE =
  'python:3.11-slim@sha256:9534e5a8e315485d4061ed659af0fd78a284c015f9b73661b41d6bab25604534';

// Both are published, real, third-party karst networks that ship with the reference
// implementation. Huttes is small enough to reason about by hand and has no looping branch;
// Sakany is large and does have them, so it exercises the paths where a branch has no defined
// tortuosity. See the NOTICE beside the fixtures for provenance and licence.
const NETWORKS = ['Huttes', 'Sakany'];

const ORACLE_DIR = join(here, 'karstnet-oracle');
const FIXTURE_DIR = join(
  repoRoot,
  'server',
  'tests',
  'SilexGis.Api.Tests',
  'Fixtures',
  'Karstnet',
);

const check = process.argv.includes('--check');

function run(workDir) {
  // Written back as the invoking user where the platform has one, so a regeneration does not
  // leave root-owned files in the working tree.
  const asUser =
    typeof process.getuid === 'function' && typeof process.getgid === 'function'
      ? ['--user', `${process.getuid()}:${process.getgid()}`]
      : [];

  // No display and no GPU: the reference library builds a matplotlib figure on import.
  const inside = [
    'pip -q install --no-cache-dir --no-deps --target /tmp/pylib -r /oracle/requirements.txt',
    `MPLBACKEND=Agg PYTHONPATH=/tmp/pylib python /oracle/oracle.py /work ${NETWORKS.join(' ')}`,
  ].join(' && ');

  const args = [
    'run',
    '--rm',
    ...asUser,
    '-e',
    'HOME=/tmp',
    '-v',
    `${ORACLE_DIR}:/oracle:ro`,
    '-v',
    `${workDir}:/work`,
    IMAGE,
    'sh',
    '-c',
    inside,
  ];

  const started = Date.now();
  const result = spawnSync('docker', args, { stdio: 'inherit' });
  const seconds = ((Date.now() - started) / 1000).toFixed(1);

  if (result.error && result.error.code === 'ENOENT') {
    console.error('docker was not found on PATH; the oracle cannot run without it.');
    process.exit(2);
  }
  if (result.status !== 0) {
    console.error(`\nThe reference implementation did not run (exit ${result.status}).`);
    process.exit(result.status ?? 1);
  }

  console.log(`\nReference run: ${seconds}s wall clock, image ${IMAGE.split('@')[0]}.`);
}

function goldenNames() {
  return readdirSync(FIXTURE_DIR)
    .filter((name) => name.endsWith('.golden.json'))
    .sort();
}

if (check) {
  const scratch = mkdtempSync(join(tmpdir(), 'karstnet-oracle-'));
  try {
    for (const network of NETWORKS) {
      for (const suffix of ['_nodes.dat', '_links.dat']) {
        cpSync(join(FIXTURE_DIR, network + suffix), join(scratch, network + suffix));
      }
    }

    run(scratch);

    const drifted = [];
    for (const name of goldenNames()) {
      const committed = readFileSync(join(FIXTURE_DIR, name), 'utf8');
      const fresh = readFileSync(join(scratch, name), 'utf8');
      if (committed !== fresh) {
        drifted.push(name);
      }
    }

    if (drifted.length > 0) {
      console.error(
        `\nThe committed reference values no longer match the pinned reference implementation:\n` +
          drifted.map((name) => `  ${name}`).join('\n') +
          `\n\nRerun without --check, read the difference, and commit it deliberately.`,
      );
      process.exit(1);
    }

    console.log(`Committed reference values agree with the reference implementation.`);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
} else {
  run(FIXTURE_DIR);
  console.log(`Wrote: ${goldenNames().join(', ')}`);
  console.log('Read the difference before committing it.');
}
