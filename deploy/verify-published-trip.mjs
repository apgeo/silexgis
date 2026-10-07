// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Ask an installation, from outside, what one published-trip link answers — the check to run
// when somebody says "the page shows nothing" or "the article's frame is blank".
// Cross-platform (Node 18+, built-ins only, no shell-isms).
//
//   node deploy/verify-published-trip.mjs https://gis.example.org
//   node deploy/verify-published-trip.mjs https://gis.example.org --origin https://club.example.org
//
// THE LINK IS NEVER AN ARGUMENT
// A follow link is the whole credential: whoever has it reads the party's names and where each
// of them was last reported. An argument is kept by the shell's history and shown to every
// account on the machine by the process list, so this takes none. Give the link — or just its
// token — one of two ways:
//   - on standard input:      a prompt asks for it, or pipe it in from a file only you can read;
//   - in the environment:     SILEXGIS_VERIFY_TOKEN (for a scheduled check; set it in the job's
//                             own environment, not on a command line).
// A whole link pasted there also says which installation to ask, so the address may be left out.
//
// Options:
//   --origin <origin>   a site that frames the page (scheme + host + port, as the browser says
//                       it); may be repeated. Each must be allowed by the page's
//                       `frame-ancestors`, or its articles show an empty box.
//   --allow-http        send the token over plain HTTP to a machine other than this one. Refused
//                       without it, because that shows the credential to the network between.
//
// WHAT IT PRINTS
// One verdict per line and nothing else:
//   ok       the thing answered as a working link's does
//   note     true and worth knowing, not a fault
//   FAIL     the reason a reader sees nothing
//   LIMITED  the server refused the request as one too many from this address. Never a pass: a
//            limited request says nothing about the link, and it does say that readers who share
//            this address are being turned away.
// The token appears in none of it. A link is named by its handle — the first characters of a
// one-way fingerprint, the same code the server's request log writes in place of the token and
// the administrators' page of published trips shows — which opens nothing and is what to search
// the API log for.
//
// Exit code 0 = no FAIL and no LIMITED line; 1 otherwise; 2 = it was not given what it needs.
//
// WHAT IT DOES NOT DO
// It reads four addresses a visitor's browser reads, once each, and the first byte of the survey
// file. It signs in to nothing, writes nothing, and cannot see the server's log: when the server
// answers a link as it answers an unknown one, the reason is on the server, under the handle.

import { createHash } from 'node:crypto';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

/** The variable a scheduled check hands the link over in. */
export const TOKEN_VARIABLE = 'SILEXGIS_VERIFY_TOKEN';

/** How long one request may take before it is reported as unanswered. */
const REQUEST_TIMEOUT_MS = 15_000;

/** The path every follow link has, in front of its token. */
const PAGE_PREFIX = '/shared/trips/';

/** The address the page behind a follow link reads, in front of the token. */
const API_PREFIX = '/api/v1/public/trips/';

/** How many characters of the fingerprint name a link — the server's own number. */
const HANDLE_LENGTH = 8;

/** Options that take no value, and the one that takes one. */
const FLAGS = ['--allow-http'];
const VALUED = ['--origin'];

/**
 * The short code a link is named by: the first characters of the base64url SHA-256 of its token.
 *
 * Computed exactly as the server computes what it stores and what its request log writes, so
 * the code printed here is the code to search that log for and the one in the administrators'
 * list. It is a prefix of a one-way hash and opens nothing.
 */
export function handleOf(token) {
  return createHash('sha256').update(token, 'utf8').digest('base64url').slice(0, HANDLE_LENGTH);
}

/**
 * What the command line asked for, or the reason it is refused.
 *
 * Anything that could be a link or a token is refused rather than used. Accepting one "just this
 * once" is how a credential ends up in a shell history: the refusal is the feature.
 */
export function parseArguments(argv) {
  const origins = [];
  let address = null;
  let allowHttp = false;

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (FLAGS.includes(arg)) {
      allowHttp = true;
    } else if (VALUED.includes(arg)) {
      const value = argv[index + 1];
      const origin = value === undefined ? null : originOf(value);
      if (origin === null || !isBareOrigin(value)) {
        return { problem: '--origin takes a site as the browser names it: scheme, host and port, no path (https://club.example.org).' };
      }
      origins.push(origin);
      index += 1;
    } else if (arg.startsWith('--')) {
      return { problem: `Unknown option ${arg}. Options: ${[...VALUED, ...FLAGS].join(', ')}.` };
    } else if (address !== null) {
      return { problem: 'One installation address at a time.' };
    } else if (!isBareOrigin(arg)) {
      // Deliberately does not echo the argument: if it was refused for looking like a link, it
      // is the very thing that must not be printed.
      return {
        problem:
          'The only argument is the installation\'s address — scheme and host, no path '
          + '(https://gis.example.org). A follow link or its token is never an argument: give it '
          + `on standard input or in ${TOKEN_VARIABLE}.`,
      };
    } else {
      address = originOf(arg);
    }
  }

  return { address, origins, allowHttp };
}

/** The origin of an address, or null when it is not an http(s) address at all. */
function originOf(text) {
  try {
    const url = new URL(text);
    return url.protocol === 'http:' || url.protocol === 'https:' ? url.origin : null;
  } catch {
    return null;
  }
}

/** True for an address that is an origin and nothing more: no path, query, fragment or login. */
function isBareOrigin(text) {
  try {
    const url = new URL(text);
    return (url.protocol === 'http:' || url.protocol === 'https:')
      && (url.pathname === '/' || url.pathname === '')
      && url.search === '' && url.hash === '' && url.username === '' && url.password === '';
  } catch {
    return false;
  }
}

/**
 * What was pasted, taken apart: the token, and the installation when a whole link was given.
 *
 * A whole link is the usual thing to have in hand — it is what the share panel copies — so it is
 * accepted as it is rather than making the operator cut the token out of it by hand, which is
 * one more place for the credential to be mistyped or left in a scratch file.
 */
export function readSecret(text) {
  const line = (text ?? '').split(/\r?\n/).map((each) => each.trim()).find((each) => each !== '') ?? '';
  if (line === '') return { problem: 'No follow link was given.' };

  const at = line.indexOf(PAGE_PREFIX);
  if (at === -1) {
    return /[\s/?#]/.test(line)
      ? { problem: `That is neither a follow link (…${PAGE_PREFIX}<token>) nor a bare token.` }
      : { token: line, address: null };
  }

  const token = line.slice(at + PAGE_PREFIX.length).split(/[/?#]/)[0];
  if (token === '') return { problem: 'That follow link has no token after its last slash.' };
  let decoded = token;
  try {
    decoded = decodeURIComponent(token);
  } catch {
    // Not percent-encoding after all; the characters as pasted are the token.
  }
  return { token: decoded, address: originOf(line.slice(0, at) + '/') };
}

/**
 * A function that takes the credential out of a line before it is printed.
 *
 * Every line this script writes goes through it, including the text of an error it did not
 * word itself, because that is the only way "the token is in nothing printed" stays true when
 * somebody adds a line later. Two things are removed: the follow token, however it is spelled
 * in an address, and the value of any `token=` parameter — the signed address of the survey file
 * is a credential too, short-lived but real.
 */
export function scrubber(token) {
  const handle = `[token:${handleOf(token)}]`;
  const spellings = [...new Set([token, encodeURIComponent(token), encodeURI(token)])]
    .filter((each) => each !== '')
    // Longest first, so a spelling that contains another is replaced whole.
    .sort((a, b) => b.length - a.length);
  return (text) => {
    let out = String(text);
    for (const spelling of spellings) out = out.split(spelling).join(handle);
    return out.replace(/([?&]token=)[^&#\s"']+/g, '$1[redacted]');
  };
}

/** True for an address on this very machine, where plain HTTP crosses no network. */
function isLoopback(address) {
  const { hostname } = new URL(address);
  return hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '[::1]';
}

/** The seconds a 429 asks the caller to wait, or null when it does not say. */
function retryAfterOf(response) {
  const seconds = Number.parseInt(response.headers.get('retry-after') ?? '', 10);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
}

/** The origins a `Content-Security-Policy` header lets frame the page, or null when it names none. */
export function frameAncestorsOf(policy) {
  const directive = (policy ?? '').split(';').map((each) => each.trim())
    .find((each) => each.toLowerCase().startsWith('frame-ancestors'));
  return directive === undefined ? null : directive.split(/\s+/).slice(1);
}

/**
 * Asks the installation what the link answers and reports one verdict per line.
 *
 * Returns whether anything failed. `out` receives whole lines already scrubbed; `fetcher` is
 * the platform's `fetch` unless a test says otherwise.
 */
export async function verify({ address, token, origins = [], linkAddress = null }, out, fetcher = fetch) {
  const scrub = scrubber(token);
  let failed = false;
  const say = (verdict, text) => {
    if (verdict === 'FAIL' || verdict === 'LIMITED') failed = true;
    out(scrub(`${verdict.padEnd(8)}${text}`));
  };

  const handle = handleOf(token);
  say('note', `link handle ${handle} — the code the server's log and the administrators' page of published trips name this link by`);
  if (linkAddress !== null && linkAddress !== address) {
    say('note', `the link pasted is of ${linkAddress}; asking ${address} as told`);
  }

  /**
   * One read. Answers the response, or null when the verdict has already been said: no answer,
   * a redirect, a 429 or a server error mean the same thing whichever address was being read.
   */
  const read = async (what, path, headers = {}) => {
    let response;
    try {
      response = await fetcher(new URL(path, address), {
        headers,
        // A redirect is reported, never followed: following one would carry the token to an
        // address nobody chose.
        redirect: 'manual',
        signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
      });
    } catch (err) {
      say('FAIL', `${what}: no answer from ${address} (${err.cause?.message ?? err.message})`);
      return null;
    }

    if (response.status === 429) {
      const wait = retryAfterOf(response);
      await response.body?.cancel();
      say('LIMITED', `${what}: rate-limited from this address`
        + (wait === null ? '' : `; the server asks for ${wait} s before another try`)
        + ' — it says nothing about the link, and readers who share this address are being turned away too');
      return null;
    }
    if (response.status >= 300 && response.status < 400) {
      await response.body?.cancel();
      say('FAIL', `${what}: answered a redirect (${response.status}) to ${response.headers.get('location') ?? 'nowhere'} — check the address; the token is not sent after it`);
      return null;
    }
    if (response.status >= 500) {
      await response.body?.cancel();
      say('FAIL', `${what}: the server answered ${response.status} — it or the proxy in front of it is not well; this is not about the link`);
      return null;
    }
    return response;
  };

  const tokenPath = encodeURIComponent(token);

  // 1. The page itself — what the web front serves, whatever the link.
  const page = await read('page', `${PAGE_PREFIX}${tokenPath}`);
  if (page !== null) {
    await page.body?.cancel();
    if (page.status !== 200) {
      say('FAIL', `page: answered ${page.status} — the web front is not serving published pages at this address`);
    } else {
      say('ok', 'page: served (this says the web front is up, not that the link is good)');
      const robots = page.headers.get('x-robots-tag') ?? '';
      if (/noindex/i.test(robots)) say('ok', 'page: tells search engines not to index it');
      else say('FAIL', 'page: no "X-Robots-Tag: noindex" — a proxy is dropping the header, and a crawler may index the party\'s names');

      const ancestors = frameAncestorsOf(page.headers.get('content-security-policy'));
      if (ancestors === null) {
        say('FAIL', 'page: no "frame-ancestors" in its Content-Security-Policy — a proxy is dropping or replacing the header');
      } else if (origins.length === 0) {
        say('note', `page: may be framed by ${ancestors.join(' ')}`);
      } else {
        for (const origin of origins) {
          if (ancestors.includes(origin) || ancestors.includes('*')) say('ok', `page: ${origin} may frame it`);
          else say('FAIL', `page: ${origin} may not frame it — its articles show an empty box; name it in SILEXGIS_FRAME_ANCESTORS (allowed now: ${ancestors.join(' ')})`);
        }
      }
    }
  }

  // 2. The link's own party.
  let followed = null;
  let model = null;
  const envelope = await read('party', `${API_PREFIX}${tokenPath}`, { accept: 'application/json' });
  if (envelope !== null) {
    if (envelope.status === 200) {
      const body = await envelope.json().catch(() => null);
      if (body === null || typeof body.state !== 'string' || typeof body.tripLogId !== 'string') {
        say('FAIL', 'party: answered 200 with something that is not a published trip — a proxy is answering in the server\'s place');
      } else {
        followed = true;
        model = body.model ?? null;
        say('ok', body.state === 'closed'
          ? 'party: the link opens its trip, which is over (tracking closed, inside the period the page keeps answering)'
          : 'party: the link follows its trip (tracking running)');
      }
    } else if (envelope.status === 404) {
      await envelope.body?.cancel();
      followed = false;
    } else {
      await envelope.body?.cancel();
      say('FAIL', `party: answered ${envelope.status}`);
    }
  }

  // 3 and 4. The cave's two lists. A link whose own trip is over still opens these.
  const list = async (what, suffix) => {
    const response = await read(what, `${API_PREFIX}${tokenPath}${suffix}`, { accept: 'application/json' });
    if (response === null) return null;
    if (response.status === 200) {
      const body = await response.json().catch(() => null);
      if (body === null || !Array.isArray(body.trips)) {
        say('FAIL', `${what}: answered 200 with something that is not a list — a proxy is answering in the server's place`);
        return null;
      }
      return body.trips.length;
    }
    await response.body?.cancel();
    if (response.status === 404) return false;
    say('FAIL', `${what}: answered ${response.status}`);
    return null;
  };

  const live = await list('followed now', '/live');
  const past = await list('past trips', '/past');

  if (typeof live === 'number') say('ok', `followed now: answered, trips listed: ${live}`);
  if (typeof past === 'number') say('ok', `past trips: answered, trips listed: ${past}`);
  if (past === false && typeof live === 'number') {
    say('note', 'past trips: not offered through this link — past trips are switched off on this installation');
  }

  if (followed === false) {
    if (typeof live === 'number' || typeof past === 'number') {
      say('note', 'party: the link no longer follows its own trip (tracking closed and the period after closing over, or tracking off) — it opens its cave\'s lists, above, and nothing of its own');
    } else if (live === false && past === false) {
      say('FAIL', `link: the server answers it exactly as it answers a link it never issued. Why is in the API log and nowhere else: search it for "link ${handle}" — the line begins "Published trip read refused" and gives the reason`);
    }
  }

  // 5. The survey drawing, by its first byte: the page is blank without it, and its address is
  // signed for a short while, so it is asked for at once.
  if (followed === true) {
    if (model === null || typeof model.modelUrl !== 'string') {
      say('note', 'survey: the trip publishes none, so the page lists the party with no drawing');
    } else {
      const file = await read('survey', model.modelUrl, { range: 'bytes=0-0' });
      if (file !== null) {
        await file.body?.cancel();
        if (file.status === 200 || file.status === 206) say('ok', 'survey: the drawing is served');
        else say('FAIL', `survey: the drawing's address answered ${file.status} — the page shows the party and an empty drawing`);
      }
    }
  }

  return failed;
}

/** The first line of standard input, asked for when a person is at the keyboard. */
async function firstLineOfStandardInput() {
  if (process.stdin.isTTY) {
    process.stderr.write('Paste the follow link (or its token) and press Enter: ');
  }
  const lines = createInterface({ input: process.stdin, terminal: false });
  for await (const line of lines) {
    lines.close();
    return line;
  }
  return '';
}

async function main() {
  const asked = parseArguments(process.argv.slice(2));
  if (asked.problem) return usage(asked.problem);

  const fromEnvironment = process.env[TOKEN_VARIABLE];
  const secret = readSecret(
    fromEnvironment !== undefined && fromEnvironment.trim() !== ''
      ? fromEnvironment
      : await firstLineOfStandardInput());
  if (secret.problem) return usage(secret.problem);

  const address = asked.address ?? secret.address;
  if (address === null) {
    return usage('Which installation? Give its address as the argument, or paste the whole follow link rather than only its token.');
  }
  if (new URL(address).protocol === 'http:' && !isLoopback(address) && !asked.allowHttp) {
    return usage(`${address} is plain HTTP: the token would cross the network readable. Use the https address, or pass --allow-http if that is really what is wanted.`);
  }

  const failed = await verify(
    { address, token: secret.token, origins: asked.origins, linkAddress: secret.address },
    (line) => console.log(line));
  process.exitCode = failed ? 1 : 0;
}

function usage(problem) {
  console.error(problem);
  console.error('Usage: node deploy/verify-published-trip.mjs [https://installation] [--origin https://site]… [--allow-http]');
  console.error(`The follow link goes on standard input or in ${TOKEN_VARIABLE}, never on the command line.`);
  process.exitCode = 2;
}

// Only when run, not when imported, so the test file can exercise the pieces above.
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch((err) => {
    // Unworded errors are not printed whole: one that quoted an address would quote the token.
    console.error(`The check itself failed: ${err.name}.`);
    process.exitCode = 2;
  });
}
