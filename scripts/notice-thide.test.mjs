// SPDX-License-Identifier: AGPL-3.0-or-later
//
// NOTICE names the ThIDE commit this repository builds and redistributes, and the submodule at
// server/external/ThIDE is what actually decides that commit. Moving the submodule without
// editing NOTICE leaves the redistributed assemblies attributed to source they were not built
// from — the MPL obligation is to point at the source of what is shipped — and nothing else
// notices, because the build is just as green either way. So the two are compared here.
//
// The commit the repository records is read from git's index (`git ls-files --stage`), not from
// the checked-out submodule: a checkout may sit anywhere, and the index is what the next commit
// will carry — in a fresh clone, as on CI, it is exactly the committed gitlink. No submodule
// checkout is needed for that.
//
// The section's heading also lists the ThIDE modules that are redistributed, and a list kept by hand
// goes stale the same way. It is compared with the ThIDE projects the server solution lists — every
// project the server takes from the submodule, directly or through another module, is listed there,
// because a Release build of the solution otherwise builds an unlisted one as Debug.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const SUBMODULE = 'server/external/ThIDE';

/**
 * The body of one NOTICE section, found by the first word of its heading. Sections are a heading
 * between two rules of dashes and a body running to the next rule.
 */
export function noticeSection(text, heading) {
  const blocks = text.replace(/\r\n/g, '\n').split(/\n-{20,}\n/);
  const at = blocks.findIndex((block) => block.trimStart().startsWith(heading));
  return at < 0 || at + 1 >= blocks.length ? undefined : blocks[at + 1];
}

/** Every full commit id a section names on a `Commit:` line. */
export function namedCommits(section) {
  return [...section.matchAll(/^\s*Commit:\s*([0-9a-f]{40})\s*$/gim)].map((m) => m[1].toLowerCase());
}

/** The commit the index records for a submodule path, from one `git ls-files --stage` line. */
export function gitlinkCommit(stageLine) {
  const match = /^160000 ([0-9a-f]{40}) \d+\t/.exec(stageLine);
  return match ? match[1] : undefined;
}

describe('the ThIDE commit NOTICE names', () => {
  it('is the commit the submodule gitlink records', () => {
    const notice = readFileSync(join(repoRoot, 'NOTICE'), 'utf8');
    const section = noticeSection(notice, 'ThIDE');
    assert.ok(section, 'NOTICE has no ThIDE section');
    const named = namedCommits(section);
    assert.equal(named.length, 1, `the ThIDE section of NOTICE should name one commit, it names ${named.length}`);

    const git = spawnSync('git', ['ls-files', '--stage', '--', SUBMODULE], { cwd: repoRoot, encoding: 'utf8' });
    assert.equal(git.status, 0, `git ls-files failed: ${git.error?.message ?? git.stderr}`);
    const recorded = gitlinkCommit(git.stdout.trim());
    assert.ok(recorded, `${SUBMODULE} is not a submodule gitlink in the index: ${JSON.stringify(git.stdout)}`);

    assert.equal(
      named[0],
      recorded,
      `NOTICE names ThIDE ${named[0]}, but ${SUBMODULE} is pinned to ${recorded}; update the Commit line`,
    );
  });
});

/** The module names a NOTICE section heading lists after its first word and dash, sorted. */
export function headingModules(text, heading) {
  const blocks = text.replace(/\r\n/g, '\n').split(/\n-{20,}\n/);
  const block = blocks.find((b) => b.trimStart().startsWith(heading));
  if (block === undefined) return undefined;
  const list = block.trim().slice(heading.length).replace(/^\s*—\s*/, '');
  return list.split(',').map((name) => name.trim()).filter(Boolean).sort();
}

/** The ThIDE project names a solution file lists, sorted. */
export function solutionThideProjects(slnx) {
  return [...slnx.matchAll(/Path="external\/ThIDE\/src\/([^/"]+)\/[^"]+\.csproj"/g)].map((m) => m[1]).sort();
}

describe('the ThIDE modules NOTICE lists', () => {
  it('are the ThIDE projects the server solution builds', () => {
    const notice = readFileSync(join(repoRoot, 'NOTICE'), 'utf8');
    const built = solutionThideProjects(readFileSync(join(repoRoot, 'server', 'SilexGis.slnx'), 'utf8'));
    assert.ok(built.length > 0, 'the server solution lists no ThIDE project');
    assert.deepEqual(headingModules(notice, 'ThIDE'), built);
  });
});

describe('reading NOTICE and the gitlink', () => {
  const sample = [
    'Header text',
    '',
    '-'.repeat(80),
    'ThIDE — Readers',
    '-'.repeat(80),
    'Body.',
    '  Commit:    ' + 'a'.repeat(40),
    '',
    '-'.repeat(80),
    'Other',
    '-'.repeat(80),
    '  Commit:    ' + 'b'.repeat(40),
    '',
  ].join('\r\n');

  it('takes the commit from the ThIDE section only, whatever the line endings', () => {
    assert.deepEqual(namedCommits(noticeSection(sample, 'ThIDE')), ['a'.repeat(40)]);
    assert.deepEqual(namedCommits(noticeSection(sample, 'Other')), ['b'.repeat(40)]);
    assert.equal(noticeSection(sample, 'Missing'), undefined);
  });

  it('reads the module list from a heading over several lines', () => {
    const text = ['Header', '-'.repeat(80), 'ThIDE — B.One, A.Two,', '        C.Three', '-'.repeat(80), 'Body.', ''].join('\n');
    assert.deepEqual(headingModules(text, 'ThIDE'), ['A.Two', 'B.One', 'C.Three']);
    assert.equal(headingModules(text, 'Missing'), undefined);
    assert.deepEqual(
      solutionThideProjects('<Project Path="external/ThIDE/src/X.Y/X.Y.csproj" /><Project Path="src/Z/Z.csproj" />'),
      ['X.Y'],
    );
  });

  it('reads a gitlink and refuses an ordinary file entry', () => {
    assert.equal(gitlinkCommit(`160000 ${'c'.repeat(40)} 0\t${SUBMODULE}`), 'c'.repeat(40));
    assert.equal(gitlinkCommit(`100644 ${'c'.repeat(40)} 0\t${SUBMODULE}`), undefined);
    assert.equal(gitlinkCommit(''), undefined);
  });
});
