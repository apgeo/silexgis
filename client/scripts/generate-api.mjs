// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Regenerates src/api/schema.d.ts from the running API's OpenAPI document.
//
//   npm run generate:api
//
// Against another checkout's API, set SILEXGIS_API_TARGET first. The two shells this project
// is developed in write that differently, and neither line works in the other:
//
//   sh:         SILEXGIS_API_TARGET=http://localhost:5099 npm run generate:api
//   PowerShell: $env:SILEXGIS_API_TARGET='http://localhost:5099'; npm run generate:api
//
// The address was written into the npm script as a literal until now, which made the script
// unusable on a machine holding more than one checkout of this project: the default port
// belongs to whichever API started first, and generating against the wrong one rewrites the
// client for a different branch's contract. Regenerating by hand with the same generator on
// another port worked, but it is not the command anyone is told to run, so the documented
// check and the executed check were not the same thing.
//
// A Node script rather than an environment reference inside the npm script, because the two
// shells this project is developed in disagree about how to write one.
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const target = (process.env.SILEXGIS_API_TARGET ?? 'http://localhost:5080').replace(/\/+$/, '');
const output = join('src', 'api', 'schema.d.ts');

console.log(`Reading the API contract from ${target}`);
const result = spawnSync(
  'npx',
  ['openapi-typescript', `${target}/openapi/v1.json`, '-o', output],
  { cwd: clientDir, stdio: 'inherit', shell: process.platform === 'win32' },
);

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}
process.exit(result.status ?? 1);
