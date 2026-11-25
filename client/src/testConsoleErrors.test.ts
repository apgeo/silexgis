// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, onTestFinished } from 'vitest';
import {
  ConsoleErrorRecord,
  consoleErrorText,
  consoleErrors,
  summariseConsoleErrors,
} from './testConsoleErrors.ts';

describe('consoleErrorText', () => {
  it('fills a format string the way the console does and appends what is left over', () => {
    expect(
      consoleErrorText(['An update to %s inside a test was not wrapped in act(...). %d%%', 'TripCard', 5, 'extra']),
    ).toBe('An update to TripCard inside a test was not wrapped in act(...). 5% extra');
  });

  it('names an error by its kind and message, and keeps a placeholder nothing fills', () => {
    expect(consoleErrorText([new TypeError('boom')])).toBe('TypeError: boom');
    expect(consoleErrorText(['failed: %s'])).toBe('failed: %s');
    expect(consoleErrorText(['%cstyled', 'color: red'])).toBe('styled');
  });

  it('survives an argument that cannot be serialised', () => {
    const circular: Record<string, unknown> = {};
    circular.self = circular;
    expect(consoleErrorText(['saw', circular])).toBe('saw [object Object]');
  });
});

describe('summariseConsoleErrors', () => {
  it('counts one error once per occurrence and tells different errors apart', () => {
    const summary = summariseConsoleErrors([
      'Could not load /api/v1/caves/123/photos for 9f1c2e4a-0000-4000-8000-000000000001',
      'Could not load /api/v1/caves/456/photos for 9f1c2e4a-0000-4000-8000-000000000002',
      'Something else went wrong',
    ]);
    // The two loads differ only in their ids, which the shared identity rule collapses.
    expect(summary.map((entry) => entry.count)).toEqual([2, 1]);
    expect(summary[0].message).toContain('Could not load');
    expect(summary[0].fingerprint).not.toBe(summary[1].fingerprint);
  });
});

describe('the per-test record', () => {
  it('attaches what a test said to that test, leaving out what it declared as expected', () => {
    consoleErrors.allow(/expected refusal/, 'this test provokes it on purpose to prove allow works');
    console.error('an expected refusal, logged by the code under test');
    console.error('an unexpected complaint about %s', 'the record itself');
    onTestFinished(({ task }) => {
      const heard = task.meta.consoleErrors ?? [];
      // Taken back off the test, so that this deliberate complaint is not counted in the run's
      // summary as if a real component had said it.
      delete task.meta.consoleErrors;
      expect(heard).toEqual([
        expect.objectContaining({ message: 'an unexpected complaint about the record itself', count: 1 }),
      ]);
    });
  });

  it('forgets the previous test’s allowance when that test ends', () => {
    console.error('an expected refusal, but this test never declared it');
    onTestFinished(({ task }) => {
      const heard = task.meta.consoleErrors ?? [];
      delete task.meta.consoleErrors;
      expect(heard.map((entry) => entry.count)).toEqual([1]);
    });
  });

  it('records nothing for a test that said nothing', () => {
    onTestFinished(({ task }) => {
      expect(task.meta.consoleErrors).toBeUndefined();
    });
  });
});

describe('a test whose after-each hook failed', () => {
  // The runner skips the remaining "after each" hooks once one throws, so the record of such a test
  // is never closed by `endTest`. Driven here on a record of its own, step by step, because making a
  // real hook throw would fail this file.
  it('neither hands its errors nor its allowances to the next test', () => {
    const record = new ConsoleErrorRecord();

    record.beginTest();
    record.allow(/refused/, 'the first test provokes a refusal on purpose');
    record.note('first test: the save was refused');
    record.note('first test: an unexpected complaint');
    // …its after-each hook throws here, so endTest is never called for it.

    record.beginTest();
    record.note('second test: the delete was refused');

    // The second test hears only what it said, and the first test's allowance no longer hides it.
    expect(record.endTest().map((entry) => entry.message)).toEqual(['second test: the delete was refused']);
    // The first test's leftovers land in the file's bucket, still filtered by its own allowance.
    expect(record.endFile().map((entry) => entry.message)).toEqual(['first test: an unexpected complaint']);
  });

  it('attributes what was said before any test began to the file, not to the first test', () => {
    const record = new ConsoleErrorRecord();
    record.note('said while the file was being set up');
    record.beginTest();
    expect(record.endTest()).toEqual([]);
    expect(record.endFile().map((entry) => entry.message)).toEqual(['said while the file was being set up']);
  });
});
