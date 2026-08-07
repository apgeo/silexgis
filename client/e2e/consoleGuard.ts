// SPDX-License-Identifier: AGPL-3.0-or-later
import { createHash } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { test as base, type Page, type TestInfo } from '@playwright/test';

// Every browser error the suite walks past, recorded.
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

/** One error as it happened, with enough of its context to be triaged without a re-run. */
export interface CapturedError {
  /** Stable short key for "this defect", so occurrences across runs and specs group together. */
  fingerprint: string;
  /** `uncaught` is a throw or a rejection nothing handled; `console` is a call to console.error. */
  kind: 'uncaught' | 'console';
  /** Error class where the browser gave one (`TypeError`), otherwise empty. */
  name: string;
  message: string;
  /** The message with per-run detail removed — what the fingerprint is taken over. */
  normalized: string;
  /** Top frame in the application's own code, line and column kept, for a human to open. */
  frame: string;
  stack: string;
  /** Address the page was on, which is also how a popup's errors are told from the main page's. */
  pageUrl: string;
  project: string;
  spec: string;
  test: string;
  at: string;
}

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

/** Absolute addresses, with Vite's cache-busting query and any trailing line:column removed. */
function stripUrls(text: string): string {
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
 * Every record this application creates is keyed by a generated identifier, and the tests create
 * their own data — so a failing request for one of them names a different address every run. Left
 * in, a defect would look new every single sweep and nothing keeping a record of them could say it
 * had seen it before. A whole path segment of nothing but digits goes the same way: there it is an
 * identifier too. Digits elsewhere are collapsed rather than removed, so two genuinely different
 * errors do not merge into one just by both mentioning a number.
 */
function collapseIds(text: string): string {
  return text
    .replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, '{id}')
    .replace(/\/\d+(?=\/|:|$)/g, '/{id}')
    .replace(/\d{4,}/g, '#');
}

/**
 * The message with everything that differs between two runs of the same defect taken out.
 *
 * Ports differ between checkouts and the development server appends a timestamp to every module it
 * serves, so the same defect would otherwise fingerprint differently on two machines.
 */
function normalizeMessage(text: string): string {
  return collapseIds(stripUrls(text)).replace(/\s+/g, ' ').trim();
}

/**
 * The first frame of the stack that is the application's own code, or the first frame at all.
 *
 * The development server serves the application from `/src/`, and everything it bundles from
 * `/node_modules/`, so the distinction is in the path. It matters because the top frame of a
 * failure inside a library is that library, which is the same for every unrelated defect that
 * happens to end up there — grouping on it would merge them all.
 */
function topAppFrame(stack: string): string {
  const frames = stack.split('\n').slice(1);
  const own = frames.find((frame) => /\/src\//.test(frame));
  const chosen = own ?? frames[0] ?? '';
  const url = /https?:\/\/[^\s)'"]+/.exec(chosen);
  return url ? new URL(url[0]).pathname : chosen.trim();
}

/**
 * Same defect, same key: the error class, the normalized message, and the file it came from.
 *
 * The file without its line and column, and with identifiers collapsed. Both matter: an edit that
 * moves the code down three lines is not a new defect, and neither is the same failing request
 * asked for a different record.
 */
function fingerprintOf(name: string, normalized: string, frame: string): string {
  const file = collapseIds(frame.replace(/:\d+:\d+$/, ''));
  return createHash('sha1').update(`${name}|${normalized}|${file}`).digest('hex').slice(0, 8);
}

/**
 * The console lines that are only the browser reporting an error already recorded, removed.
 *
 * One uncaught error produces two records: the thrown error, with its stack, and the line the
 * browser prints about it, which contains the same message and points at nothing useful. Left
 * alone they fingerprint differently — different error class, different frame — so one defect would
 * be filed as two, one of them unlocatable.
 *
 * Done when the test ends rather than as the events arrive, because it must not depend on which of
 * the two the browser raises first, and that order is not something to rely on. A message too short
 * to be distinctive is left alone: matching on it would drop console errors that merely happen to
 * contain the same handful of characters.
 */
function withoutEchoes(errors: CapturedError[]): CapturedError[] {
  const thrown = errors
    .filter((error) => error.kind === 'uncaught' && error.message.length >= 12)
    .map((error) => error.message);
  return errors.filter(
    (error) => error.kind !== 'console' || !thrown.some((message) => error.message.includes(message)),
  );
}

/** A page's console and its uncaught errors, recorded into `into`. */
function watchPage(page: Page, into: CapturedError[], testInfo: TestInfo) {
  const record = (kind: CapturedError['kind'], name: string, message: string, stack: string) => {
    const normalized = normalizeMessage(message);
    const frame = topAppFrame(stack || message);
    into.push({
      fingerprint: fingerprintOf(name, normalized, frame),
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
export const test = base.extend<{ consoleErrors: ConsoleErrorGuard }>({
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
