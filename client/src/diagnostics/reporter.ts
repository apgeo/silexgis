// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  fingerprintOf,
  normalizeMessage,
  stripUrls,
  topAppFrame,
  withoutEchoes,
  type ErrorKind,
  type ErrorRecord,
} from './errorIdentity.ts';
import { breadcrumbTrail, installBreadcrumbs, recordBreadcrumb } from './breadcrumbs.ts';

// The application reporting its own errors while somebody is using it.
//
// The end-to-end suite watches the console on the flows it drives; this covers the rest, which is
// everything a person does that no spec does. Reports go to a file on the development machine through
// a route the development server answers — see the sink plugin in the Vite configuration. Nothing
// leaves the machine, there is no service and no account, and outside development this does not run
// at all: the address it posts to exists only while the development server does.
//
// Two rules this code must never break, because it is the thing that watches for breakage:
//
//   1. It must not throw. Every listener body is wrapped, and a failure to report is silent.
//   2. It must not report itself. Reporting calls `console.error` indirectly through anything it
//      touches, and a wrapper that reported its own reports would not stop.

/** Where the development server takes reports. Answered by nothing in a production build. */
const SINK_URL = '/__client-errors';

/** How long related errors are collected before being sent, in milliseconds. */
const BATCH_DELAY = 500;

/** A cap so that a loop reporting thousands of times cannot fill a disk or a request body. */
const MAX_PER_BATCH = 50;

/** Frames belonging to this diagnostic, which sit above every error it captures. */
const OWN_FRAMES = /\/src\/diagnostics\//;

/**
 * Set by this diagnostic's own end-to-end spec, which is the one automated run that must report.
 *
 * Declared on the window because the spec has to set it before any of the application's code runs,
 * and an init script is the only thing that happens that early.
 */
declare global {
  interface Window {
    __silexgisReportUnderAutomation?: boolean;
  }
}

/**
 * True when a driver is running the browser, in which case this stands down.
 *
 * The end-to-end suite watches the console from outside and records exactly the same errors, so a
 * second channel from inside the application would add nothing except to fill the browsing log with
 * hundreds of records from test runs — and the value of that log is precisely that everything in it
 * was seen by a person using the application. `navigator.webdriver` is what every driver sets.
 */
function drivenByAutomation(): boolean {
  return navigator.webdriver === true && window.__silexgisReportUnderAutomation !== true;
}

let enabled = false;
let reporting = false;
let pending: ErrorRecord[] = [];
let flushTimer: ReturnType<typeof setTimeout> | undefined;

function send(batch: ErrorRecord[]): void {
  // `keepalive` so a report survives the navigation that often follows the error that caused it.
  // Failures are swallowed on purpose: the sink is absent in a production build and unreachable
  // whenever the development server is restarting, and neither is worth telling anybody about.
  void fetch(SINK_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(batch),
    keepalive: true,
  }).catch(() => undefined);
}

function scheduleFlush(): void {
  if (flushTimer !== undefined) {
    return;
  }
  flushTimer = setTimeout(() => {
    flushTimer = undefined;
    // The delay is what makes the echo rule work: a throw and the console line about it land in the
    // same batch, so the duplicate can be dropped without guessing which of them arrives first.
    const batch = withoutEchoes(pending);
    pending = [];
    if (batch.length > 0) {
      send(batch);
    }
  }, BATCH_DELAY);
}

/**
 * Records one error and queues it for the sink.
 *
 * Exported because a React error boundary is the one observer that cannot be a listener: React hands
 * a caught error to a component rather than letting it reach the window, and the component stack it
 * comes with — which names the component tree that failed — exists nowhere else.
 */
export function reportError(observed: {
  kind: ErrorKind;
  name?: string;
  message: string;
  stack?: string;
  componentStack?: string;
}): void {
  if (!enabled || reporting) {
    return;
  }
  reporting = true;
  try {
    const stack = observed.stack ?? '';
    const normalized = normalizeMessage(observed.message);
    const frame = topAppFrame(stack || observed.message, OWN_FRAMES);
    const name = observed.name ?? '';
    const record: ErrorRecord = {
      fingerprint: fingerprintOf({ kind: observed.kind, name, normalized, frame }),
      source: 'browsing',
      kind: observed.kind,
      name,
      message: observed.message,
      normalized,
      frame,
      stack: stripUrls(stack),
      pageUrl: location.href,
      at: new Date().toISOString(),
      breadcrumbs: breadcrumbTrail(),
      userAgent: navigator.userAgent,
      viewport: `${window.innerWidth}x${window.innerHeight}`,
    };
    if (observed.componentStack) {
      record.componentStack = stripUrls(observed.componentStack);
    }
    if (pending.length < MAX_PER_BATCH) {
      pending.push(record);
      scheduleFlush();
    }
  } catch {
    // A diagnostic that fails is a diagnostic that reported nothing, not an error of its own.
  } finally {
    reporting = false;
  }
}

/** The message a rejected promise carries, for the many things that can be rejected with. */
function describeRejection(reason: unknown): { message: string; stack: string; name: string } {
  if (reason instanceof Error) {
    return { message: reason.message, stack: reason.stack ?? '', name: reason.name };
  }
  // A promise can be rejected with anything at all, and a rejection with a non-error carries no stack
  // anywhere: the message is all there will ever be. Prefixed so that it reads as what it is, since
  // `String(reason)` on its own is frequently something like "[object Object]".
  return { message: `Unhandled rejection: ${String(reason)}`, stack: '', name: '' };
}

/**
 * Starts watching, and starts the breadcrumb trail.
 *
 * Call once, as early as possible — before the application renders, so that an error thrown while it
 * is starting up is reported rather than being the one class of error this never sees.
 */
export function installErrorReporting(): void {
  if (enabled || drivenByAutomation()) {
    return;
  }
  enabled = true;
  installBreadcrumbs();

  window.addEventListener('error', (event) => {
    // Also fires for a subresource that failed to load, where there is no error object and the
    // target is the element that could not load. Those are the browser's business rather than a
    // fault in the code, and the suite records them from outside anyway.
    if (!event.error && event.target !== window) {
      return;
    }
    const error: unknown = event.error;
    reportError(
      error instanceof Error
        ? { kind: 'uncaught', name: error.name, message: error.message, stack: error.stack }
        : { kind: 'uncaught', message: event.message || String(error) },
    );
  });

  window.addEventListener('unhandledrejection', (event) => {
    const { message, stack, name } = describeRejection(event.reason);
    reportError({ kind: 'uncaught', name, message, stack });
  });

  // Wrapping `console.error` is what catches everything a library reports rather than throws — which
  // is where React and the component library say a great deal that matters. The stack is taken here,
  // at the call, because nothing downstream can recover it afterwards.
  const original = console.error.bind(console);
  console.error = (...args: unknown[]) => {
    original(...args);
    try {
      const message = args
        .map((arg) => (arg instanceof Error ? arg.message : typeof arg === 'string' ? arg : safeText(arg)))
        .join(' ')
        .trim();
      const carried = args.find((arg): arg is Error => arg instanceof Error);
      reportError({
        kind: 'console',
        message,
        stack: carried?.stack ?? new Error('console.error').stack ?? '',
      });
    } catch {
      // As everywhere here: the console call itself already happened, which is what mattered.
    }
  };

  recordBreadcrumb('note', 'error reporting started');
}

/** A value rendered for a message, without letting a cyclic or exotic object throw. */
function safeText(value: unknown): string {
  try {
    return JSON.stringify(value) ?? String(value);
  } catch {
    return String(value);
  }
}
