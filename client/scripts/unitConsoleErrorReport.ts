// SPDX-License-Identifier: AGPL-3.0-or-later
import { writeFileSync } from 'node:fs';
import type { Reporter, TestModule } from 'vitest/node';

// The end-of-run summary of the console errors the unit tests provoked.
//
// Each test file runs in a worker of its own, so no single place in the tests sees the whole run;
// what each test heard travels to this process on the test's own metadata (the setup file's
// console guard attaches it), and is added up here once every file has finished. It is a report
// only: it never changes the run's verdict. It prints how many errors were heard, in how many
// files, and each distinct one with its count — distinct by the identity the guard computed, which
// is the same one the browser suite uses. Setting SILEXGIS_UNIT_CONSOLE_REPORT to a file path also
// writes the whole record there as JSON, per file and per test, for whoever is clearing the
// backlog.

interface HeardError {
  fingerprint: string;
  message: string;
  count: number;
}

interface Distinct {
  message: string;
  count: number;
  files: Set<string>;
}

interface FileRecord {
  file: string;
  count: number;
  tests: { test: string; errors: HeardError[] }[];
}

function heardBy(meta: object): HeardError[] {
  return (meta as { consoleErrors?: HeardError[] }).consoleErrors ?? [];
}

const OUTSIDE_A_TEST = '(outside any test)';

export class UnitConsoleErrorReport implements Reporter {
  onTestRunEnd(testModules: ReadonlyArray<TestModule>): void {
    const files: FileRecord[] = [];
    const distinct = new Map<string, Distinct>();
    for (const module of testModules) {
      const record: FileRecord = { file: module.relativeModuleId, count: 0, tests: [] };
      const add = (test: string, errors: HeardError[]) => {
        if (errors.length === 0) {
          return;
        }
        record.tests.push({ test, errors });
        for (const error of errors) {
          record.count += error.count;
          const known = distinct.get(error.fingerprint) ?? {
            message: error.message,
            count: 0,
            files: new Set<string>(),
          };
          known.count += error.count;
          known.files.add(record.file);
          distinct.set(error.fingerprint, known);
        }
      };
      for (const test of module.children.allTests()) {
        add(test.fullName, heardBy(test.meta()));
      }
      add(OUTSIDE_A_TEST, heardBy(module.meta()));
      if (record.count > 0) {
        files.push(record);
      }
    }

    const total = files.reduce((sum, file) => sum + file.count, 0);
    const lines = [
      '',
      ` Console errors (report only, the run's verdict does not depend on them): ${total} in ${files.length} test file(s), ${distinct.size} distinct`,
    ];
    const byCount = [...distinct.entries()].sort((a, b) => b[1].count - a[1].count);
    for (const [fingerprint, entry] of byCount) {
      lines.push(`   ${String(entry.count).padStart(5)} × in ${entry.files.size} file(s) [${fingerprint}] ${entry.message}`);
    }
    if (files.length > 0) {
      lines.push('   by file:');
      for (const file of [...files].sort((a, b) => b.count - a.count)) {
        lines.push(`   ${String(file.count).padStart(5)}  ${file.file}`);
      }
    }
    process.stdout.write(`${lines.join('\n')}\n`);

    const target = process.env.SILEXGIS_UNIT_CONSOLE_REPORT;
    if (target) {
      const distinctList = byCount.map(([fingerprint, entry]) => ({
        fingerprint,
        message: entry.message,
        count: entry.count,
        files: [...entry.files].sort(),
      }));
      writeFileSync(target, `${JSON.stringify({ total, distinct: distinctList, files }, null, 2)}\n`);
    }
  }
}
