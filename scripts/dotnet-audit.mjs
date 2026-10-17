// SPDX-License-Identifier: AGPL-3.0-or-later

// Fails when any project in the solution references a NuGet package with a known vulnerability,
// transitively included — unless that advisory is one the build itself has been told to accept.
//
// `dotnet list package --vulnerable` exits 0 whether or not it found anything, so its exit code
// proves nothing; what it reports does, and it is read in its JSON form rather than scraped.
//
// What counts as accepted is read from one place: the `NuGetAuditSuppress` items in
// server/Directory.Build.props, the list `dotnet restore` already honours. The listing command does
// not honour them, so without this a restore that is clean by decision and an audit that fails on
// the same advisories would disagree for as long as the decision stands. An accepted advisory is
// still printed, by name, every run; a suppression is a decision somebody can be asked about, not a
// silence. Run from the repository root.

import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import process from 'node:process';

/** The advisory URLs the build has been told to accept, from a Directory.Build.props text. */
export function acceptedAdvisories(propsText) {
  return new Set(
    [...propsText.matchAll(/<NuGetAuditSuppress\s+Include="([^"]+)"/g)].map((m) => m[1].trim()),
  );
}

/**
 * Every (project, package, advisory) the listing reports, split into the accepted and the rest.
 * Paths are reported relative to `root` so the output reads the same on every machine.
 */
export function judge(listing, accepted, root = '') {
  const found = [];
  for (const project of listing.projects ?? []) {
    for (const framework of project.frameworks ?? []) {
      for (const pkg of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) {
        for (const advisory of pkg.vulnerabilities ?? []) {
          found.push({
            project: root && project.path.startsWith(root) ? project.path.slice(root.length + 1) : project.path,
            id: pkg.id,
            version: pkg.resolvedVersion,
            severity: advisory.severity,
            url: advisory.advisoryurl,
          });
        }
      }
    }
  }
  return {
    accepted: found.filter((f) => accepted.has(f.url)),
    refused: found.filter((f) => !accepted.has(f.url)),
  };
}

/** One line per package and advisory, the projects that reach it named once. */
export function describe(findings) {
  const byKey = new Map();
  for (const f of findings) {
    const key = `${f.id} ${f.version} ${f.severity} ${f.url}`;
    if (!byKey.has(key)) byKey.set(key, new Set());
    byKey.get(key).add(f.project);
  }
  return [...byKey].map(([key, projects]) => `  ${key}  (${projects.size} project${projects.size === 1 ? '' : 's'})`);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const server = join(process.cwd(), 'server');
  const r = spawnSync(
    'dotnet',
    ['list', 'SilexGis.slnx', 'package', '--vulnerable', '--include-transitive', '--format', 'json'],
    { cwd: server, encoding: 'utf8', shell: process.platform === 'win32', maxBuffer: 64 * 1024 * 1024 },
  );
  process.stderr.write(r.stderr || '');
  if (r.status !== 0) {
    process.stdout.write(r.stdout || '');
    console.error(`dotnet list package exited ${r.status}`);
    process.exit(r.status ?? 1);
  }
  const listing = JSON.parse(r.stdout);
  const accepted = acceptedAdvisories(readFileSync(join(server, 'Directory.Build.props'), 'utf8'));
  const { accepted: known, refused } = judge(listing, accepted, server);
  if (known.length > 0) {
    console.log('Accepted by a NuGetAuditSuppress in server/Directory.Build.props:');
    for (const line of describe(known)) console.log(line);
  }
  if (refused.length > 0) {
    console.error('\nVulnerable NuGet packages are referenced and not accepted anywhere:');
    for (const line of describe(refused)) console.error(line);
    process.exit(1);
  }
  console.log(`\nNo vulnerable NuGet package referenced that the build has not been told to accept.`);
}
