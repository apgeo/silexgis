// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterAll, afterEach, beforeEach } from 'vitest';
import { fingerprintOf, normalizeMessage } from './diagnostics/errorIdentity.ts';

// Every `console.error` a unit test provokes, recorded — in report-only mode for now.
//
// React says "this update was not wrapped in act", "a component is changing an uncontrolled input"
// or "each child in a list should have a unique key" by writing to `console.error` and carrying on,
// and so does any component that catches its own failure and logs it. A test that provokes one
// still passes, the line scrolls past among thousands, and the defect it names is never looked at.
// So every unexpected one is recorded against the test that was running and summed up once at the
// end of the run (the reporter beside the test configuration prints it). Nothing fails yet: the
// suite has an unknown backlog of these, and turning the gate on before that backlog is counted
// and cleared would make the whole suite red at once. The switch to failing comes later and
// changes only what the end of the run does with the count.
//
// Two kinds of `console.error` are not recorded here because they already fail the test on their
// own (the antd deprecation notice and the icon-only button without a label); the setup file
// decides that and calls `noteConsoleError` only for the rest.
//
// What counts as the same error is decided by the one shared identity rule the browser suite and
// the in-app reporter use, so a defect seen by both the unit and the browser suite has the same
// fingerprint in both summaries.
//
// Known blind spot: a test that replaces `console.error` with a spy that swallows its arguments
// (`vi.spyOn(console, 'error').mockImplementation(() => {})`) hides everything said while the spy
// is installed. A spy that calls through, or one that is restored, does not.

/** One distinct console error as the reporter receives it: identity, a readable line, how often. */
export interface UnitConsoleError {
  fingerprint: string;
  /** The first line of the message, shortened — for the summary, not for identity. */
  message: string;
  count: number;
}

declare module 'vitest' {
  interface TaskMeta {
    consoleErrors?: UnitConsoleError[];
  }
}

interface Allowance {
  pattern: RegExp;
  reason: string;
}

const ARGUMENT_LIMIT = 500;

function describeArgument(value: unknown): string {
  if (value instanceof Error) {
    return `${value.name}: ${value.message}`;
  }
  if (typeof value === 'string') {
    return value;
  }
  let text: string;
  try {
    text = JSON.stringify(value) ?? String(value);
  } catch {
    // A circular structure (a DOM node, a React element with its owner) cannot be serialised.
    text = String(value);
  }
  return text.length > ARGUMENT_LIMIT ? `${text.slice(0, ARGUMENT_LIMIT)}…` : text;
}

/**
 * The line `console.error(...args)` would print, built the way the console builds it: a first
 * string argument is a format whose `%s`-style placeholders take the arguments that follow, and
 * whatever is left over is appended. React writes its warnings this way, so without the
 * substitution every one of them would read "%s" where the component's name belongs.
 */
export function consoleErrorText(args: readonly unknown[]): string {
  const [first, ...rest] = args;
  if (typeof first !== 'string') {
    return args.map(describeArgument).join(' ');
  }
  let next = 0;
  const text = first.replace(/%[sdifoOc%]/g, (token) => {
    if (token === '%%') {
      return '%';
    }
    if (next >= rest.length) {
      return token;
    }
    const value = rest[next];
    next += 1;
    return token === '%c' ? '' : describeArgument(value);
  });
  return [text, ...rest.slice(next).map(describeArgument)].join(' ');
}

/** Groups what was heard by the shared identity rule, keeping the order each was first heard in. */
export function summariseConsoleErrors(texts: readonly string[]): UnitConsoleError[] {
  const byFingerprint = new Map<string, UnitConsoleError>();
  for (const text of texts) {
    const normalized = normalizeMessage(text);
    const fingerprint = fingerprintOf({ kind: 'console', name: '', normalized, frame: '' });
    const known = byFingerprint.get(fingerprint);
    if (known) {
      known.count += 1;
    } else {
      const firstLine = normalized.length > 0 ? text.trim().split('\n')[0] : '';
      byFingerprint.set(fingerprint, {
        fingerprint,
        message: firstLine.length > 200 ? `${firstLine.slice(0, 200)}…` : firstLine,
        count: 1,
      });
    }
  }
  return [...byFingerprint.values()];
}

/**
 * What the tests of one file said, split by the test that was running.
 *
 * A test's record is closed by an "after each" hook, and the runner skips the remaining "after
 * each" hooks of a test once one of them throws — which the setup file's deprecation check and many
 * files' own checks do by design. So a test's record is also closed when the NEXT test begins:
 * whatever is still open then is moved to the file's own bucket (filtered by the allowances of the
 * test that said it, which were still in force), and the allowances are dropped. Without that, a
 * test whose hook failed handed both its unflushed errors and its `allow()` patterns to the test
 * after it, which then had errors it never said and a licence to ignore ones it did.
 */
export class ConsoleErrorRecord {
  private allowances: Allowance[] = [];
  private heard: string[] = [];
  private outsideTests: string[] = [];

  note(text: string): void {
    this.heard.push(text);
  }

  allow(pattern: RegExp, reason: string): void {
    this.allowances.push({ pattern, reason });
  }

  private unexpected(texts: readonly string[]): string[] {
    // `search` ignores a pattern's `lastIndex`, so a pattern written with the `g` flag answers the
    // same way every time it is asked.
    return texts.filter((text) => !this.allowances.some((entry) => text.search(entry.pattern) >= 0));
  }

  /** A test begins: anything still open was said outside a test, or by a test whose hook failed. */
  beginTest(): void {
    this.outsideTests.push(...this.unexpected(this.heard));
    this.heard = [];
    this.allowances = [];
  }

  /** A test ends normally: what it said, less what it declared as expected. */
  endTest(): UnitConsoleError[] {
    const errors = summariseConsoleErrors(this.unexpected(this.heard));
    this.heard = [];
    this.allowances = [];
    return errors;
  }

  /** The file ends: what was said outside any test, or left behind by a test whose hook failed. */
  endFile(): UnitConsoleError[] {
    const errors = summariseConsoleErrors([...this.outsideTests, ...this.heard]);
    this.outsideTests = [];
    this.heard = [];
    return errors;
  }
}

const record = new ConsoleErrorRecord();

/** Called by the setup file's `console.error` wrapper for every line it does not fail on itself. */
export function noteConsoleError(args: readonly unknown[]): void {
  record.note(consoleErrorText(args));
}

export const consoleErrors = {
  /**
   * Declares a `console.error` this test provokes on purpose — a failure path that logs, a library
   * that complains about input the test feeds it deliberately — with the reason it is expected.
   * It holds for the test it is called in (directly or from a `beforeEach`) and is forgotten when
   * that test ends; the reason is there for the reader, so it has to say why, not what.
   */
  allow(pattern: RegExp, reason: string): void {
    record.allow(pattern, reason);
  },
};

/**
 * Registers the hooks that attach what each test said to that test, and what was said outside any
 * test to its file. The setup file calls this before it registers anything else: "before" hooks
 * run in the order they were registered, so a test's own `beforeEach` can declare an allowance after
 * this one has cleared the last test's; "after" hooks run in reverse order, so these run last and
 * also hear what the unmount after each test provokes.
 */
export function installConsoleErrorReport(): void {
  beforeEach(() => {
    record.beginTest();
  });
  afterEach(({ task }) => {
    const errors = record.endTest();
    if (errors.length > 0) {
      task.meta.consoleErrors = errors;
    }
  });
  // The runner reads the first parameter's destructuring to decide which fixtures a hook needs, so
  // it has to be written as a pattern even though this hook needs none of them.
  // oxlint-disable-next-line no-empty-pattern
  afterAll(({}, file) => {
    const errors = record.endFile();
    if (errors.length > 0) {
      file.meta.consoleErrors = errors;
    }
  });
}
