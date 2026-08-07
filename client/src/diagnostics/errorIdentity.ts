// SPDX-License-Identifier: AGPL-3.0-or-later

// What makes two browser errors the same defect.
//
// This rule has to live in exactly one place because two entirely different things apply it: the
// application reporting its own errors while somebody uses it, and the end-to-end suite watching the
// console while it drives the same screens. If the two computed identity even slightly differently,
// every defect seen both ways would be recorded twice — once per observer — and a record of known
// defects would be unable to say whether anything was new. So this module is imported by both, and
// neither has a copy.
//
// It is deliberately free of anything browser- or Node-specific: no DOM, no `node:crypto`, no
// timers. The hash below is not a cryptographic one for the same reason — it only has to be stable
// and the same everywhere, and a few dozen distinct messages are nowhere near where a 32-bit key
// starts colliding.

/** Where an error was observed from. Both observers record the same shape. */
export type ErrorSource = 'browsing' | 'sweep';

/** `uncaught` is a throw or a rejection nothing handled; `console` is a call to `console.error`. */
export type ErrorKind = 'uncaught' | 'console';

/** One error as it happened, with enough context to be understood without reproducing it. */
export interface ErrorRecord {
  /** Stable short key for "this defect", so occurrences group across observers and runs. */
  fingerprint: string;
  source: ErrorSource;
  kind: ErrorKind;
  /** Error class where the browser gave one (`TypeError`), otherwise empty. */
  name: string;
  message: string;
  /** The message with per-run detail removed — part of what the fingerprint is taken over. */
  normalized: string;
  /** Best frame available, kept with its line and column for a human to open. */
  frame: string;
  stack: string;
  /** Address the page was on, which is also how a popped-out window's errors are told apart. */
  pageUrl: string;
  at: string;
  /** Present when the application reported it: what the person did just before. */
  breadcrumbs?: string[];
  /** Present when a React error boundary caught it. */
  componentStack?: string;
  userAgent?: string;
  viewport?: string;
  /** Present when the end-to-end suite recorded it. */
  project?: string;
  spec?: string;
  test?: string;
}

/** Absolute addresses reduced to their path, with any trailing line:column removed. */
export function stripUrls(text: string): string {
  return text.replace(/https?:\/\/[^\s)'"]+/g, (candidate) => {
    try {
      return new URL(candidate).pathname.replace(/:\d+:\d+$/, '');
    } catch {
      return candidate;
    }
  });
}

/**
 * Identifiers replaced by a placeholder, so the same defect keeps one identity between runs.
 *
 * Every record this application creates is keyed by a generated identifier, so a failing request for
 * one of them names a different address every time. Left in, a defect would look new on every
 * observation and nothing keeping a record of them could say it had seen it before. A whole path
 * segment of nothing but digits goes the same way: there it is an identifier too. Digits elsewhere
 * are collapsed rather than removed, so two genuinely different errors do not merge into one just by
 * both mentioning a number.
 */
export function collapseIds(text: string): string {
  return text
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '{id}')
    .replace(/\/\d+(?=\/|:|$)/g, '/{id}')
    .replace(/\d{4,}/g, '#');
}

/**
 * The message with everything that differs between two sightings of the same defect taken out.
 *
 * Ports differ between checkouts and the development server appends a cache-busting query to every
 * module it serves, so the same defect would otherwise be identified differently on two machines.
 */
export function normalizeMessage(text: string): string {
  return collapseIds(stripUrls(text)).replace(/\s+/g, ' ').trim();
}

/**
 * The most useful frame of a stack: the first one in the application's own code, or failing that
 * the first frame at all.
 *
 * The development server serves the application from `/src/` and everything it bundles from
 * `/node_modules/`, so the distinction is in the path. It matters because the top frame of a failure
 * inside a library is that library, which is the same for every unrelated defect that happens to end
 * up there — identifying on it would merge them all.
 *
 * `ignore` exists for the observer that is itself part of the application: the reporting code sits in
 * the stack of every error it captures, and would otherwise name itself as the source of all of them.
 */
export function topAppFrame(stack: string, ignore?: RegExp): string {
  const frames = stack
    .split('\n')
    .slice(1)
    .filter((frame) => !ignore?.test(frame));
  const own = frames.find((frame) => /\/src\//.test(frame));
  const chosen = own ?? frames[0] ?? '';
  const url = /https?:\/\/[^\s)'"]+/.exec(chosen);
  if (!url) {
    return chosen.trim();
  }
  try {
    return new URL(url[0]).pathname;
  } catch {
    return chosen.trim();
  }
}

/**
 * True when a frame is an address the browser blamed instead of a call site.
 *
 * A browser reporting that a request failed puts the failing address where a call site would go —
 * and that address is the only thing telling one failed request from another, since the message
 * itself says nothing but the status. Our own code and the development server's own modules are not
 * that; see `fingerprintOf` for why telling them apart matters.
 */
function isReportedAddress(frame: string): boolean {
  return frame.startsWith('/') && !frame.startsWith('/src/') && !frame.startsWith('/@');
}

/** Stable, short, and the same in both observers. Not a cryptographic hash and does not need to be. */
function hash(text: string): string {
  let value = 0x811c9dc5;
  for (let index = 0; index < text.length; index += 1) {
    value ^= text.charCodeAt(index);
    value = Math.imul(value, 0x01000193);
  }
  return (value >>> 0).toString(16).padStart(8, '0');
}

/**
 * The console lines that are only something reporting an error already recorded, removed.
 *
 * One uncaught error produces two observations. The browser prints a line about every error that
 * reaches it, and React prints one about every error a boundary catches — so alongside the throw,
 * with its stack and its class, there is a console message repeating the same text and pointing at
 * nothing useful. Left alone the two are different defects by the rule above, so a single failure
 * would be recorded twice, one of them unlocatable.
 *
 * Applied when a batch is about to be written rather than as each error arrives, because it must not
 * depend on which of the two comes first — and that order is decided by the browser and by React, not
 * by us. A message too short to be distinctive is left alone: matching on it would discard console
 * errors that merely happen to share a few characters.
 */
export function withoutEchoes<T extends { kind: ErrorKind; message: string }>(records: T[]): T[] {
  const thrown = records
    .filter((record) => record.kind === 'uncaught' && record.message.length >= 12)
    .map((record) => record.message);
  return records.filter(
    (record) => record.kind !== 'console' || !thrown.some((message) => record.message.includes(message)),
  );
}

/**
 * The identity of a defect, from the parts of an observation that both observers agree on.
 *
 * The two kinds are keyed differently, and the reason is that the observers know different amounts
 * about each:
 *
 * **A throw** is keyed by its class, its message and the file it came from. Both observers read
 * those from the same `Error` object raised by the same browser, so both arrive at the same key.
 *
 * **A `console.error` is keyed by its message alone** — plus the failing address, when the browser
 * supplied one instead of a call site. This is the case that would otherwise file one defect twice.
 * The application knows exactly which of its own lines called `console.error`; the suite watching
 * from outside is told only that the development server's console wrapper did, because that wrapper
 * is what actually called it. One of those is useful and the other is noise, and they are never the
 * same string — so neither takes part in the identity, and the message decides. The address does
 * take part, because a browser-reported request failure carries nothing else that distinguishes it,
 * and only the outside observer ever sees those at all.
 *
 * Line and column are dropped throughout: an edit that moves code down three lines is not a new
 * defect.
 */
export function fingerprintOf(observation: {
  kind: ErrorKind;
  name: string;
  normalized: string;
  frame: string;
}): string {
  const frame = observation.frame.replace(/:\d+:\d+$/, '');
  if (observation.kind === 'uncaught') {
    return hash(`${observation.name}|${observation.normalized}|${collapseIds(frame)}`);
  }
  const address = isReportedAddress(frame) ? collapseIds(frame) : '';
  return hash(`|${observation.normalized}|${address}`);
}
