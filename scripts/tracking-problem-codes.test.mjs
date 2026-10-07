// SPDX-License-Identifier: AGPL-3.0-or-later

// The refusals the tracking routes answer with, and the sentences the application has for them,
// are the same list — checked here because nothing else can.
//
// Why this exists. The server names a refusal by a stable code; the application keeps one sentence
// per code and shows a general "that could not be saved" for a code it has no sentence for. The
// two lists live in two programs written in two languages, so neither program's own tests can read
// the other's. They drifted exactly as that predicts: six codes were answered with no sentence at
// all, while a comment beside the application's table said a check held the two sets equal — and
// the only check there was compared the table with the translation files, never with the server.
//
// What is held:
//   1. the server writes each code down once, in one file of constants, and nowhere else;
//   2. every code there has a sentence in the application's table;
//   3. the table names no tracking code the server does not answer with.
// Both translations having each sentence is the application's own test (it reads the same table).
//
// Both sides are read as text rather than imported: one is C#, and importing the other would pull
// in the application's whole toolchain to answer a question about a list of names. The parses are
// narrow and fail loudly when a file's shape stops matching, rather than quietly checking nothing;
// the last block proves each of them on invented text, so that a green run here means the checks
// can see a difference and not merely that they ran.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { strict as assert } from 'node:assert';
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const serverSrc = join(repoRoot, 'server', 'src');
const constantsPath = join(serverSrc, 'SilexGis.Domain', 'Trips', 'TrackingProblemCodes.cs');
const tablePath = join(repoRoot, 'client', 'src', 'components', 'trips', 'trackingProblems.ts');

/** A tracking code written out as a C# string: the whole literal, quotes included. */
const QUOTED_CODE = /"tracking\.[a-z0-9_]+"/g;

/** Every quoted tracking code in a piece of C# source. */
function quotedCodesIn(source) {
  return [...source.matchAll(QUOTED_CODE)].map((match) => match[0].slice(1, -1));
}

/**
 * The constants a C# source declares as `public const string Name = "tracking.code";`, and how
 * many `const string` declarations it holds in all — the two counts differ when one is written in
 * a shape this parse does not read, which must fail rather than drop the constant from the list.
 */
function constantsIn(source) {
  const declared = [...source.matchAll(/public const string (\w+) = "(tracking\.[a-z0-9_]+)";/g)].map(
    (match) => ({ name: match[1], code: match[2] }),
  );
  const declarations = [...source.matchAll(/\bconst string\b/g)].length;
  return { declared, declarations };
}

/** `picture_not_found` → `PictureNotFound`: what a constant is called, given its code. */
function nameFor(code) {
  return code
    .slice('tracking.'.length)
    .split('_')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join('');
}

/**
 * The codes the application's table has sentences for: the keys of the one object literal, read
 * between its declaration and its closing line. Throws when the declaration is not found, because
 * an empty list from a renamed table would otherwise read as "the server has codes nobody words".
 */
function tableCodesIn(source) {
  const block = /export const TRACKING_PROBLEM_MESSAGE_KEYS[^=]*=\s*\{([\s\S]*?)\n\};/.exec(source);
  if (!block) {
    throw new Error('the table of tracking refusal sentences was not found where it is expected');
  }
  return [...block[1].matchAll(/^\s*'([a-z0-9_.]+)':\s*'[A-Za-z0-9_.]+',?\s*$/gm)].map((match) => match[1]);
}

/** Every `.cs` file under a directory, skipping build output. */
function csharpFilesUnder(dir) {
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (entry.name === 'bin' || entry.name === 'obj') continue;
      found.push(...csharpFilesUnder(join(dir, entry.name)));
    } else if (entry.name.endsWith('.cs')) {
      found.push(join(dir, entry.name));
    }
  }
  return found;
}

const { declared, declarations } = constantsIn(readFileSync(constantsPath, 'utf8'));
const serverCodes = declared.map((constant) => constant.code).sort();

describe('the tracking refusal codes', () => {
  it('are each written down once, under the name their code gives them', () => {
    assert.ok(declared.length >= 20, `expected the real list, read ${declared.length} constants`);
    assert.equal(declarations, declared.length, 'a constant is declared in a shape this check does not read');
    assert.deepEqual(serverCodes, [...new Set(serverCodes)], 'one code is declared twice');
    for (const constant of declared) {
      assert.equal(constant.name, nameFor(constant.code), `${constant.code} is not called what its code says`);
    }
  });

  it('are written out nowhere else in the server', () => {
    const files = csharpFilesUnder(serverSrc);
    assert.ok(files.length > 500, `expected the server's source, saw ${files.length} files`);
    const strays = [];
    for (const file of files) {
      if (file === constantsPath) continue;
      for (const code of quotedCodesIn(readFileSync(file, 'utf8'))) {
        strays.push(`${relative(repoRoot, file).split(sep).join('/')}: "${code}"`);
      }
    }
    assert.deepEqual(strays, [], 'a tracking code is written out where it is answered: use its constant');
  });

  it('each have a sentence in the application, and the application words none the server does not answer', () => {
    const tableCodes = tableCodesIn(readFileSync(tablePath, 'utf8'));
    const worded = tableCodes.filter((code) => code.startsWith('tracking.')).sort();
    assert.ok(tableCodes.length > worded.length, 'the table lost the codes of the other families it words');
    assert.deepEqual(
      serverCodes.filter((code) => !worded.includes(code)),
      [],
      'the server answers with a code the application has no sentence for',
    );
    assert.deepEqual(
      worded.filter((code) => !serverCodes.includes(code)),
      [],
      'the application keeps a sentence for a code the server does not answer with',
    );
  });
});

describe('the checks above can see what they claim to', () => {
  it('a code written out in an answer is found, and prose that merely mentions one is not', () => {
    assert.deepEqual(quotedCodesIn('return ApiProblems.NotFound("tracking.team_not_found");'), [
      'tracking.team_not_found',
    ]);
    assert.deepEqual(quotedCodesIn('refused[id] = cond ? "tracking.a_b" : "tracking.c";'), [
      'tracking.a_b',
      'tracking.c',
    ]);
    assert.deepEqual(quotedCodesIn('"A link taken back cannot: 409 tracking.share_revoked."'), []);
    assert.deepEqual(quotedCodesIn('return ApiProblems.NotFound(TrackingProblemCodes.TeamNotFound);'), []);
  });

  it('a constant in another shape is counted as unread rather than dropped', () => {
    const read = constantsIn(
      'public const string TeamNotFound = "tracking.team_not_found";\n' +
        'internal const string Other =\n    "tracking.other";\n',
    );
    assert.deepEqual(read.declared, [{ name: 'TeamNotFound', code: 'tracking.team_not_found' }]);
    assert.equal(read.declarations, 2);
    assert.equal(nameFor('tracking.no_station_at_depth'), 'NoStationAtDepth');
  });

  it('the table is read whole, comments and other families included, and its absence is an error', () => {
    const table =
      "export const TRACKING_PROBLEM_MESSAGE_KEYS: Record<string, string> = {\n" +
      "  'tracking.not_writable': 'trips.tracking.problems.notWritable',\n" +
      "  // A comment naming 'tracking.in_a_comment': 'not.a.row' in passing.\n" +
      "  'tracking.share_revoked': 'trips.tracking.problems.shareRevoked',\n" +
      "  'trip_log.not_found': 'trips.tracking.problems.tripNotFound',\n" +
      '};\n' +
      "const later = { 'tracking.after_the_table': 'x.y' };\n";
    assert.deepEqual(tableCodesIn(table), ['tracking.not_writable', 'tracking.share_revoked', 'trip_log.not_found']);
    assert.throws(() => tableCodesIn('export const SOMETHING_ELSE = {};'));
  });
});
