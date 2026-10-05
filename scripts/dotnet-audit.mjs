// SPDX-License-Identifier: AGPL-3.0-or-later

// Fails when any project in the solution references a NuGet package with a known vulnerability,
// transitively included. `dotnet list package --vulnerable` exits 0 whether or not it found
// anything, so its exit code proves nothing; what it prints does. Run from the repository root.

import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import process from 'node:process';

const server = join(process.cwd(), 'server');
const r = spawnSync(
  'dotnet',
  ['list', 'SilexGis.slnx', 'package', '--vulnerable', '--include-transitive'],
  { cwd: server, encoding: 'utf8', shell: process.platform === 'win32' },
);
process.stdout.write(r.stdout || '');
process.stderr.write(r.stderr || '');
if (r.status !== 0) {
  console.error(`dotnet list package exited ${r.status}`);
  process.exit(r.status ?? 1);
}
if (/has the following vulnerable packages/i.test(r.stdout)) {
  console.error('\nVulnerable NuGet packages are referenced — see above.');
  process.exit(1);
}
console.log('\nNo vulnerable NuGet packages referenced.');
