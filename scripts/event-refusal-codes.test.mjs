// SPDX-License-Identifier: AGPL-3.0-or-later

// The refusals the event routes answer with, and the sentences the application has for them, are
// one list — checked here because nothing else can.
//
// The server names a refusal by a stable code; the application's event surfaces keep one sentence
// per code and show a general "save failed" or "delete failed" for a code they have no sentence for.
// That fallback is exactly what an organiser meets when the server adds a refusal nobody worded:
// a weekly course given a ten-day span was refused with a precise reason and shown "save failed".
// The two lists live in two programs written in two languages, so neither program's own tests can
// read the other's.
//
// What is held: every event code the server source spells out is either worded in the
// application's table or listed beside it as one no event surface can meet, with its reason; and
// neither list names a code the server does not have. Both translations having each sentence is
// the application's own test.
//
// Both sides are read as text. The parses are narrow and fail loudly when a file's shape stops
// matching; the last block proves them on invented text, so a green run means the check can see a
// difference and not merely that it ran.
//
// Run from the repository root with `node --test "scripts/**/*.test.mjs"`.

import { strict as assert } from 'node:assert';
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const serverSrc = join(repoRoot, 'server', 'src');
const tablePath = join(repoRoot, 'client', 'src', 'pages', 'events', 'eventRefusals.ts');

/** Every quoted event code in a piece of C# source. */
function serverCodesIn(source) {
  return [...source.matchAll(/"(event\.[a-z0-9_]+)"/g)].map((match) => match[1]);
}

/** Every C# file under a directory, build output left out. */
function csFiles(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (entry.name !== 'bin' && entry.name !== 'obj') out.push(...csFiles(join(dir, entry.name)));
    } else if (entry.name.endsWith('.cs')) {
      out.push(join(dir, entry.name));
    }
  }
  return out;
}

/**
 * The codes one exported object literal of the client file names, read between its declaration
 * and its closing line. Throws when the declaration is missing, because an empty list from a
 * renamed table would otherwise read as "the server has codes nobody words".
 */
function objectCodesIn(source, name) {
  const block = new RegExp(`export const ${name}[^=]*=\\s*\\{([\\s\\S]*?)\\n\\};`).exec(source);
  if (!block) {
    throw new Error(`${name} was not found where it is expected`);
  }
  return [...block[1].matchAll(/^\s*'(event\.[a-z0-9_]+)':/gm)].map((match) => match[1]);
}

describe('event refusal codes', () => {
  const server = new Set(csFiles(serverSrc).flatMap((file) => serverCodesIn(readFileSync(file, 'utf8'))));
  const client = readFileSync(tablePath, 'utf8');
  const worded = objectCodesIn(client, 'EVENT_REFUSALS');
  const unworded = objectCodesIn(client, 'EVENT_CODES_WITHOUT_WORDS');

  it('finds the server codes at all', () => {
    assert.ok(server.size >= 10, `only ${server.size} event codes were found in the server source`);
  });

  it('accounts for every server code: worded, or listed as unreachable with a reason', () => {
    const accounted = new Set([...worded, ...unworded]);
    const missing = [...server].filter((code) => !accounted.has(code)).sort();
    assert.deepEqual(missing, [], `server event codes with no sentence and no stated reason: ${missing.join(', ')}`);
  });

  it('names no code the server does not answer with', () => {
    const unknown = [...worded, ...unworded].filter((code) => !server.has(code)).sort();
    assert.deepEqual(unknown, [], `client lists event codes the server has not got: ${unknown.join(', ')}`);
  });

  it('does not both word a code and call it unreachable', () => {
    const both = worded.filter((code) => unworded.includes(code));
    assert.deepEqual(both, []);
  });

  it('the parses see what they are meant to see (invented text)', () => {
    assert.deepEqual(serverCodesIn('const string A = "event.made_up"; var b = "eventual.no";'), ['event.made_up']);
    const invented = [
      "export const EVENT_REFUSALS: Readonly<Record<string, string>> = {",
      "  'event.one': 'events.one',",
      "  // a comment",
      "  'event.two': 'events.two',",
      '};',
    ].join('\n');
    assert.deepEqual(objectCodesIn(invented, 'EVENT_REFUSALS'), ['event.one', 'event.two']);
    assert.throws(() => objectCodesIn(invented, 'EVENT_CODES_WITHOUT_WORDS'));
  });
});
