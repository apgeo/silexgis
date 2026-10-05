// SPDX-License-Identifier: AGPL-3.0-or-later

// Where a test run's hours went, read off the runner's own trx file: wall clock per test class,
// longest first, with the test count and the slowest single test of each.
//
// A full API integration run prints nothing but failures, so for a run measured in hours the
// only record of its cost was its total. This reads the per-test timings the `trx` logger
// writes — the gate's full runs and the CI shards both ask for one — so a decision about the
// suite (what to split, what to hoist, how to re-cut the CI deal) rests on a measurement rather
// than on an estimate. A trx is XML; this reads only the two elements it needs, with no parser
// dependency.
//
// Usage: node scripts/trx-timings.mjs <file.trx> [--top <n>] [--json]

import { readFileSync } from 'node:fs';
import process from 'node:process';

/** Per-class totals from a trx document. Exported for the test. */
export function classTimings(xml) {
  // <UnitTest id="…" name="Method"> … <TestMethod className="Ns.Class" name="Method"/> …
  const classById = new Map();
  for (const m of xml.matchAll(/<UnitTest\b[^>]*\bid="([^"]+)"[\s\S]*?<TestMethod\b[^>]*\bclassName="([^"]+)"/g)) {
    classById.set(m[1], m[2].split(',')[0].trim());
  }
  // <UnitTestResult … testId="…" … duration="hh:mm:ss.fffffff" … outcome="Passed" …/>
  const byClass = new Map();
  for (const m of xml.matchAll(/<UnitTestResult\b([^>]*)\/?>/g)) {
    const attrs = m[1];
    const id = /\btestId="([^"]+)"/.exec(attrs)?.[1];
    const duration = /\bduration="([^"]+)"/.exec(attrs)?.[1];
    const outcome = /\boutcome="([^"]+)"/.exec(attrs)?.[1] ?? '';
    const name = /\btestName="([^"]+)"/.exec(attrs)?.[1] ?? '';
    const cls = classById.get(id);
    if (!cls || !duration) continue;
    const seconds = toSeconds(duration);
    const row = byClass.get(cls) ?? { className: cls, tests: 0, failed: 0, seconds: 0, slowest: { name: '', seconds: 0 } };
    row.tests += 1;
    row.seconds += seconds;
    if (outcome !== 'Passed' && outcome !== 'NotExecuted') row.failed += 1;
    if (seconds > row.slowest.seconds) row.slowest = { name, seconds };
    byClass.set(cls, row);
  }
  return [...byClass.values()].sort((a, b) => b.seconds - a.seconds);
}

function toSeconds(hms) {
  const [h, m, s] = hms.split(':');
  return Number(h) * 3600 + Number(m) * 60 + Number(s);
}

function main() {
  const args = process.argv.slice(2);
  const file = args.find((a) => !a.startsWith('--'));
  if (!file) {
    console.error('usage: node scripts/trx-timings.mjs <file.trx> [--top <n>] [--json]');
    process.exit(1);
  }
  const topAt = args.indexOf('--top');
  const top = topAt === -1 ? 30 : Number(args[topAt + 1]);
  const rows = classTimings(readFileSync(file, 'utf8'));
  const total = rows.reduce((s, r) => s + r.seconds, 0);
  const tests = rows.reduce((s, r) => s + r.tests, 0);
  if (args.includes('--json')) {
    console.log(JSON.stringify({ file, classes: rows.length, tests, testSeconds: Math.round(total), rows }, null, 2));
    return;
  }
  console.log(`${rows.length} classes, ${tests} tests, ${(total / 3600).toFixed(2)} test-hours summed over tests (not wall clock)\n`);
  console.log('   seconds  tests  failed  class  (slowest test)');
  for (const r of rows.slice(0, top)) {
    const short = r.className.replace(/^SilexGis\.Api\.Tests\./, '');
    console.log(
      `${String(Math.round(r.seconds)).padStart(10)}  ${String(r.tests).padStart(5)}  ${String(r.failed).padStart(6)}  ${short}  (${r.slowest.name} ${Math.round(r.slowest.seconds)}s)`,
    );
  }
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].split(/[\\/]/).pop())) {
  main();
}
