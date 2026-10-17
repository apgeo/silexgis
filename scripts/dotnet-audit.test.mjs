// SPDX-License-Identifier: AGPL-3.0-or-later
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { test } from 'node:test';

import { acceptedAdvisories, describe, judge } from './dotnet-audit.mjs';

const listing = (packages) => ({
  projects: [
    { path: '/repo/server/src/A/A.csproj', frameworks: [{ framework: 'net10.0', transitivePackages: packages }] },
    { path: '/repo/server/tests/B/B.csproj' },
  ],
});
const imageSharp = (...urls) => ({
  id: 'SixLabors.ImageSharp',
  resolvedVersion: '2.1.11',
  vulnerabilities: urls.map((url) => ({ severity: 'High', advisoryurl: url })),
});

test('an advisory the build accepts is reported as accepted, and does not fail the audit', () => {
  const accepted = acceptedAdvisories('<NuGetAuditSuppress Include="https://x/GHSA-1" />');
  const { accepted: known, refused } = judge(listing([imageSharp('https://x/GHSA-1')]), accepted, '/repo/server');
  assert.equal(refused.length, 0);
  assert.deepEqual(known.map((f) => [f.project, f.id, f.url]), [['src/A/A.csproj', 'SixLabors.ImageSharp', 'https://x/GHSA-1']]);
});

test('a new advisory on the same package is not accepted by the old ones', () => {
  // The suppressions are by advisory, never by package: a sixth advisory against the same library
  // is a new question, and the audit has to ask it.
  const accepted = acceptedAdvisories('<NuGetAuditSuppress Include="https://x/GHSA-1" />');
  const { refused } = judge(listing([imageSharp('https://x/GHSA-1', 'https://x/GHSA-2')]), accepted);
  assert.deepEqual(refused.map((f) => f.url), ['https://x/GHSA-2']);
});

test('the projects reaching one advisory are counted once in the report', () => {
  const twice = {
    projects: ['A', 'B'].map((name) => ({
      path: `/s/${name}.csproj`,
      frameworks: [{ framework: 'net10.0', transitivePackages: [imageSharp('https://x/GHSA-1')] }],
    })),
  };
  assert.deepEqual(describe(judge(twice, new Set()).refused), [
    '  SixLabors.ImageSharp 2.1.11 High https://x/GHSA-1  (2 projects)',
  ]);
});

test('the list is read from the props file the build itself uses', () => {
  // The audit and the restore must agree about what is accepted, so the audit reads the build's
  // own list rather than a second one; this proves the reading finds what the file holds.
  const props = readFileSync(join(dirname(fileURLToPath(import.meta.url)), '..', 'server', 'Directory.Build.props'), 'utf8');
  const declared = [...props.matchAll(/NuGetAuditSuppress/g)].length;
  assert.equal(acceptedAdvisories(props).size, declared);
});
