// SPDX-License-Identifier: AGPL-3.0-or-later
import { writeFileSync } from 'node:fs';
import { test as base, type Page, type TestInfo } from '@playwright/test';
import {
  fingerprintOf,
  normalizeMessage,
  stripUrls,
  topAppFrame,
  withoutEchoes,
  type ErrorKind,
  type ErrorRecord,
} from '../src/diagnostics/errorIdentity.ts';
import { CHOICE_KEY } from '../src/i18n/languageStorage.ts';

// Every browser error the suite walks past, recorded.
//
// What counts as "the same defect" is NOT decided here. It comes from the application's own
// diagnostics module, because the application reports its own errors too while somebody is using it —
// and if these two observers identified a defect even slightly differently, everything seen both ways
// would be recorded twice and nothing could say whether a defect was new. There is one rule and this
// imports it.
//
// A spec asserts what it was written to assert, so a page that throws while doing it passes as
// readily as one that does not: nothing in Playwright fails a test because the application
// reported an error to its console. That gap is why errors are found by a person browsing and
// reported by hand. These flows already walk most of the application, so watching the console
// while they do costs one listener and turns each pass into a sweep.
//
// Two modes, chosen by SILEXGIS_CONSOLE_GATE:
//
//   report (the default) — errors are recorded, printed, and written next to the test's other
//     artifacts. The run's result is unchanged. This is the mode to be in while the backlog of
//     what the sweep finds is still being worked through: a gate that fails the whole suite on
//     the first day would be turned off on the first day.
//   enforce — the same, and a test that saw an unexplained error fails. This is where the
//     default belongs once that backlog is worked through; until then a green run does not mean
//     silence.
//
// What a test EXPECTS to see is declared in the test, with its reason — see `allow` below. What
// no test should have to declare is in ALLOWED_EVERYWHERE. Nothing else is filtered: an error
// that turns out to be harmless is either explained in one of those two places or it is a
// defect, and "we have all seen that one before" is not a third category.
const gateMode = process.env.SILEXGIS_CONSOLE_GATE === 'enforce' ? 'enforce' : 'report';

/**
 * Errors no test should have to excuse, each with the reason it is here.
 *
 * Deliberately empty to begin with. An entry earns its place by being understood — a browser or
 * library reporting something the application cannot act on — and never by being frequent.
 */
const ALLOWED_EVERYWHERE: { pattern: RegExp; reason: string }[] = [];

/**
 * One error as the suite saw it — the shared record shape, with the suite's own fields filled in.
 *
 * The same shape the application writes when it reports an error about itself, so that both end up in
 * one place and group by the same key. `spec`, `test` and `project` are what only this observer knows;
 * breadcrumbs and a component stack are what only the other one does.
 */
export type CapturedError = ErrorRecord;

export interface ConsoleErrorGuard {
  /**
   * Declares that this test expects an error matching `pattern`, and why.
   *
   * For the flows that drive a failure on purpose — a refused request, a viewer told to open
   * something broken — where the error in the console is the application behaving correctly.
   * The reason is required because it is the only thing separating this from silencing a defect,
   * and it is recorded in the run's artifacts so a later reader can judge it.
   *
   * May be called at any point in the test, including after the error has already appeared:
   * what was allowed is applied when the test ends, not when the error arrives.
   */
  allow(pattern: RegExp, reason: string): void;
  /** Everything captured so far that no `allow` and no global entry accounts for. */
  captured(): readonly CapturedError[];
}

/** A page's console and its uncaught errors, recorded into `into`. */
function watchPage(page: Page, into: CapturedError[], testInfo: TestInfo) {
  const record = (kind: ErrorKind, name: string, message: string, stack: string) => {
    const normalized = normalizeMessage(message);
    const frame = topAppFrame(stack || message);
    into.push({
      fingerprint: fingerprintOf({ kind, name, normalized, frame }),
      source: 'sweep',
      kind,
      name,
      message,
      normalized,
      frame,
      stack: stripUrls(stack),
      // Read rather than awaited: a listener that awaits anything can be running while the page
      // is being closed, and `url()` is the one accessor that cannot fail for that reason.
      pageUrl: page.url(),
      project: testInfo.project.name,
      spec: testInfo.titlePath[0] ?? '',
      test: testInfo.title,
      at: new Date().toISOString(),
    });
  };

  // A throw and a rejection nothing handled both arrive here, which is why this is not just a
  // console listener: an uncaught error is reported to the console too, but only this event
  // carries the error object, and with it the stack the frame is read from.
  page.on('pageerror', (error) => {
    record('uncaught', error.name, error.message, error.stack ?? '');
  });

  page.on('console', (message) => {
    if (message.type() !== 'error') {
      return;
    }
    // An uncaught error arrives here as well, as the line the browser prints about it, and that
    // line is worth strictly less: it carries no error object, so no stack, so no frame in our own
    // code. It is recorded anyway and dropped when the test ends — see `withoutEchoes`, which can
    // compare the two without depending on which event the browser raises first.
    const where = message.location();
    record(
      'console',
      '',
      message.text(),
      where.url ? `\n    at ${where.url}:${where.lineNumber}:${where.columnNumber}` : '',
    );
  });
}

/**
 * The suite's `test`, with the console watched for every test that uses it.
 *
 * Specs import `test` from here rather than from Playwright directly. That is a real cost — a
 * new spec that forgets is silently unwatched — and it is the only way Playwright offers to
 * reach every page a test opens. The step that groups a run's recorded errors reports any spec
 * that is not importing this, so the omission surfaces when the errors are read rather than
 * staying invisible forever.
 */
export const test = base.extend<{ consoleErrors: ConsoleErrorGuard; readsEnglish: void }>({
  /**
   * The suite reads English, and asks for it before anything is drawn.
   *
   * The application opens in Romanian deliberately, and reaches English only through a recorded
   * choice — not by running in a browser that happens to be configured in English. Every
   * assertion in this suite names a string and those strings are written in English, so the
   * choice has to be made somewhere. Made here it is made once, before the first navigation, for
   * every page the context opens including the windows the application pops out.
   *
   * Without it the failure is not "the label is in Romanian": the shared sign-in helper waits
   * out its timeout on a field labelled `Password` over a form that says `Parolă`, and every
   * desktop spec fails on the same line of the same helper with nothing naming the language.
   *
   * The key is imported rather than spelled again, for the reason its own module gives for
   * existing: the two disagreeing would be silent — the choice would be written where nothing
   * reads it and the suite would quietly be back in Romanian. This is the same statement a
   * person makes by picking English in the header, not a test-only back door.
   */
  readsEnglish: [
    async ({ context }, use) => {
      // Wrapped, because an init script runs on every document the context loads — `about:blank`
      // and any sandboxed frame among them — and touching `localStorage` there throws "Access is
      // denied for this document". That throw reaches the console, so an unguarded version would
      // manufacture the very errors the guard below exists to catch.
      await context.addInitScript((key: string) => {
        try {
          window.localStorage.setItem(key, 'en');
        } catch {
          // A document with no storage is not one the application runs in.
        }
      }, CHOICE_KEY);
      await use();
    },
    { auto: true },
  ],
  consoleErrors: [
    async ({ context }, use, testInfo) => {
      const captured: CapturedError[] = [];
      const allowed: { pattern: RegExp; reason: string }[] = [];
      const watched = new WeakSet<Page>();

      const watch = (page: Page) => {
        if (!watched.has(page)) {
          watched.add(page);
          watchPage(page, captured, testInfo);
        }
      };

      // The context rather than the page, so that a window the application pops out is watched
      // too — the multi-window flows are exactly where a second window's errors would otherwise
      // go unseen. Both routes are taken because which of them yields the first page depends on
      // whether this fixture is set up before Playwright creates it.
      context.on('page', watch);
      context.pages().forEach(watch);


      const unexplained = () =>
        withoutEchoes(captured).filter(
          (error) =>
            !allowed.some((entry) => entry.pattern.test(error.message)) &&
            !ALLOWED_EVERYWHERE.some((entry) => entry.pattern.test(error.message)),
        );

      await use({
        allow: (pattern, reason) => allowed.push({ pattern, reason }),
        captured: unexplained,
      });

      const found = unexplained();
      if (found.length === 0) {
        return;
      }

      // One line per error, in the test's own artifact directory — which Playwright empties
      // before each run, so what is there afterwards is this run and not an accumulation.
      const path = testInfo.outputPath('console-errors.jsonl');
      writeFileSync(path, `${found.map((error) => JSON.stringify(error)).join('\n')}\n`, 'utf8');
      await testInfo.attach('console-errors.jsonl', { path, contentType: 'application/x-ndjson' });

      const groups = [...new Set(found.map((error) => error.fingerprint))];
      const summary = `${found.length} console error(s), ${groups.length} distinct`;
      testInfo.annotations.push({ type: 'console-errors', description: summary });
      // Printed as well as attached: an artifact nobody opens is not a report, and the point of
      // the reporting mode is that the errors are seen during an ordinary run.
      process.stdout.write(
        `\n  ${summary} in "${testInfo.title}" [${testInfo.project.name}]\n${found
          // The frame as well as the message: "a resource failed to load" says only that one did,
          // and the frame is the only thing separating one missing file from another.
          .map((error) => `    ${error.fingerprint}  ${error.message.split('\n')[0]} [${error.frame}]`)
          .join('\n')}\n`,
      );

      if (gateMode === 'enforce') {
        throw new Error(
          `${summary}. Fix them, or declare the expected ones with consoleErrors.allow(pattern, reason).\n` +
            found.map((error) => `  [${error.fingerprint}] ${error.message}`).join('\n'),
        );
      }
    },
    { auto: true },
  ],
});
