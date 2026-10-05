// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * A write on a trip replaces every column it carries, and more than one surface saves a trip
 * whole: the form, the sections card that echoes the rest of the trip back around the section it
 * edits, and the history restore that casts the trip as read into the write. A column added to the
 * write and produced by only one of them is dropped by the others on their next save, silently.
 *
 * What keeps that from happening is the compiler, and this test pins the three things the
 * compiler's protection rests on: that the generated write type has no optional member (a
 * defaulted member on the server would publish as optional and stop being enforced); that the trip
 * as read still carries every column the write takes (the restore surface sends the read trip as
 * the write); and that the two surfaces that build a body by hand build it as a literal annotated
 * with the write type, which is the line the compiler checks — a spread or a cast would pass with
 * a column missing.
 */
// Read off the working tree the way the type checker reads it; the test runner's root is the
// client package, which is where every other path in this suite is resolved from.
const schema = readFileSync(path.resolve(process.cwd(), 'src/api/schema.d.ts'), 'utf8');

/** The members of one generated schema block, each with whether it is optional. */
function membersOf(block: string): Map<string, boolean> {
  const start = schema.indexOf(`        ${block}: {`);
  expect(start, `${block} is in the generated schema`).toBeGreaterThan(-1);
  const end = schema.indexOf('\n        };', start);
  const members = new Map<string, boolean>();
  for (const line of schema.slice(start, end).split('\n')) {
    const member = /^ {12}([A-Za-z]+)(\?)?:/.exec(line);
    if (member) {
      members.set(member[1], member[2] === '?');
    }
  }
  expect(members.size).toBeGreaterThan(0);
  return members;
}

describe('the columns a trip write carries', () => {
  const write = membersOf('TripLogWriteRequest');

  it('are every one required, so a surface that leaves one out does not compile', () => {
    const optional = [...write].filter(([, isOptional]) => isOptional).map(([name]) => name);
    expect(optional).toEqual([]);
  });

  it('are all carried by the trip as read, which the history restore sends back as the write', () => {
    const read = membersOf('TripLogDto');
    const missing = [...write.keys()].filter((name) => !read.has(name));
    expect(missing).toEqual([]);
  });

  it('are built by both saving surfaces as a literal the compiler checks against the write type', () => {
    for (const surface of ['TripFormModal.tsx', 'TripSections.tsx']) {
      const source = readFileSync(path.resolve(process.cwd(), 'src/pages/trips', surface), 'utf8');
      expect(source, surface).toContain('const body: TripLogWrite = {');
    }
  });
});
