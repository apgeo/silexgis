// SPDX-License-Identifier: AGPL-3.0-or-later
import createClient from 'openapi-fetch';
import type { paths } from './schema';
import { userManager } from '../auth/auth.tsx';
import { recordBreadcrumb } from '../diagnostics/breadcrumbs.ts';
import i18n from '../i18n';

/** Thrown by the unwrap helpers so callers can react to the HTTP status / problem code. */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  /**
   * What the server wrote about this particular refusal, when it wrote anything.
   *
   * Kept because a few refusals are only useful in their own words — a layout the parser could
   * not read names the line that is wrong, and no wording this client holds could say that. Never
   * shown by default: a screen that has a phrase of its own for a code shows that phrase, and this
   * is for the cases where the server knows something the client cannot.
   */
  readonly detail?: string;

  /**
   * The refusal's own members, as the server wrote them.
   *
   * A few refusals carry a fact a screen has to act on rather than merely show — the identifier
   * of the document a duplicate upload collided with, say. That fact belongs here and not in
   * `detail`: the detail is a sentence written for a person to read, so recovering a value by
   * matching it out of that prose makes rewording or translating the sentence a silent break in
   * whatever was parsing it. Read through {@link member}, which is where the narrowing lives.
   */
  readonly problem?: Readonly<Record<string, unknown>>;

  /**
   * How long the server asked to be left alone before this request is made again, in
   * milliseconds — or undefined where it named no wait.
   *
   * A server that refuses for being asked too often usually says when to come back, in the
   * response's `Retry-After`. Asking again sooner is not merely wasted: each early attempt is
   * counted against the very allowance the refusal was about, so a client that ignores the number
   * keeps itself refused. Read once, where the response is in hand, by {@link retryAfterOf}.
   */
  readonly retryAfterMs?: number;

  constructor(
    status: number,
    code?: string,
    detail?: string,
    problem?: Record<string, unknown>,
    retryAfterMs?: number,
  ) {
    super(`API error ${status}${code ? ` (${code})` : ''}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.problem = problem;
    this.retryAfterMs = retryAfterMs;
  }

  /** One member of the refusal, when the server sent it and it is a string. */
  member(name: string): string | undefined {
    const value = this.problem?.[name];
    return typeof value === 'string' ? value : undefined;
  }
}

/** A lost-update / precondition conflict on a protected write (see the optimistic-concurrency contract). */
export function isConcurrencyConflict(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.status === 412 || error.status === 428 || (error.code?.startsWith('concurrency.') ?? false))
  );
}

/**
 * Whether the server has answered this request for good.
 *
 * A 4xx is an answer rather than a mishap: the identical request cannot produce anything else, so
 * nothing at all is gained by asking again. Rate limiting is the one client error that is not
 * settled — 429 clears by itself once the window rolls over. Network faults and 5xx are not
 * settled either; they are the ordinary consequence of a dropped connection or a server that is
 * coming back.
 *
 * <b>One home, because two different decisions turn on it and they must not disagree.</b> The
 * retry policy below reads it to decide whether to ask again, and a screen holding data from an
 * earlier read reads it to decide whether a failure is a blip to wait out or an answer to act on.
 * A page that stopped retrying while still telling its reader "it starts refreshing again by
 * itself as soon as it can" would be the application contradicting itself, over a table that will
 * never refresh — and the reader would be the person who has to act on the difference.
 */
export function isSettledRefusal(error: unknown): boolean {
  return (
    error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 429
  );
}

/**
 * How often a failed read is attempted again. A settled refusal is not attempted again at all —
 * the three default attempts would merely hold the screen in its loading state for the whole
 * 1s + 2s + 4s backoff before the failure finally becomes visible. Everything else keeps the
 * retries.
 */
export function retryQuery(failureCount: number, error: unknown): boolean {
  if (isSettledRefusal(error)) {
    return false;
  }
  return failureCount < 3;
}

/**
 * The longest wait a response is allowed to name. A header is text from the network: one that
 * said a day, by a misconfigured proxy or a clock that is wrong, would otherwise leave a screen
 * waiting on a request that looks in flight until somebody reloads it.
 */
export const RETRY_AFTER_CEILING_MS = 5 * 60_000;

/** A date as HTTP writes one: `Mon, 14 Sep 2026 12:00:45 GMT`. */
const HTTP_DATE = /^[A-Za-z]{3}, \d{2} [A-Za-z]{3} \d{4} \d{2}:\d{2}:\d{2} GMT$/;

/**
 * The wait a response names in its `Retry-After`, in milliseconds, or undefined where it names
 * none that can be read.
 *
 * The header has two spellings and both are in use: a whole number of seconds, which is what a
 * rate limiter writes, and a date, which is what a proxy in front of a server that is down for
 * maintenance tends to write. A date already past means "now". Anything else — an empty value, a
 * fraction, a negative number, words — is no wait at all rather than a guess, so the caller falls
 * back to its own pacing exactly as if the header were absent.
 */
export function retryAfterOf(
  response: { headers?: Pick<Headers, 'get'> },
  now = Date.now(),
): number | undefined {
  // The header list is asked for and not assumed: a reply that did not come from a browser's own
  // transport may carry none, and that is "no wait named", not a fault of its own.
  const written = response.headers?.get('Retry-After')?.trim();
  if (!written) {
    return undefined;
  }
  if (/^\d+$/.test(written)) {
    return Math.min(Number(written) * 1000, RETRY_AFTER_CEILING_MS);
  }
  // Only the one date spelling HTTP writes today. The engine's own date reading is far more
  // generous than that — it makes a year out of "-5" and a day out of "1.5" — and a wait
  // conjured from a value nobody meant as a date is worse than no wait.
  if (!HTTP_DATE.test(written)) {
    return undefined;
  }
  const at = Date.parse(written);
  if (Number.isNaN(at)) {
    return undefined;
  }
  return Math.min(Math.max(at - now, 0), RETRY_AFTER_CEILING_MS);
}

/**
 * How long to wait before attempt number `failureCount` of a read that failed.
 *
 * <b>The server's number where it gave one, and otherwise the wait this client always used.</b>
 * A read refused for being asked too often is retried — rate limiting clears by itself — and
 * retried a second, two and four later it spends three more requests of the allowance it was just
 * told is used up, which is how a page that was merely busy becomes a page that stays refused. So
 * a refusal that says when to come back is taken at its word. Everything else doubles from one
 * second, capped at thirty: the query library's own pacing, written out because supplying this
 * function replaces it.
 */
export function retryDelay(failureCount: number, error: unknown): number {
  if (error instanceof ApiError && error.retryAfterMs !== undefined) {
    return error.retryAfterMs;
  }
  return Math.min(1000 * 2 ** failureCount, 30_000);
}

/**
 * Typed API client generated from the server OpenAPI contract.
 * `schema.d.ts` is generated — regenerate with `npm run generate:api`, never hand-edit.
 */
export const api = createClient<paths>({ baseUrl: '/' });

// The signed-in account, on every request. Named and exported rather than written inline, because
// one other caller has to make a request the same way this client does — see the read below.
export const carriesTheAccountsBearer = {
  async onRequest({ request }: { request: Request }) {
    const user = await userManager.getUser();
    if (user?.access_token) {
      request.headers.set('Authorization', `Bearer ${user.access_token}`);
    }
    return request;
  },
};

api.use(carriesTheAccountsBearer);

// The language the person is reading the site in, on every request.
//
// Some answers are written by the server rather than by this client — the lines in the inbox are,
// so that an operator who rewrites a message is read in the new wording without a client release.
// The server picks the language from this header first and only then from the language the
// account last saved, which is the right order: somebody who switches the site to Romanian on a
// browser installed in English wants Romanian now, not at their next sign-in. Without the header
// they would get their browser's language instead, and a page half in each.
export const sendsTheReadingLanguage = {
  onRequest({ request }: { request: Request }) {
    const language = i18n.resolvedLanguage ?? i18n.language;
    if (language) {
      request.headers.set('Accept-Language', language);
    }
    return request;
  },
};

api.use(sendsTheReadingLanguage);

// Optimistic-concurrency threading: remember the version each answer carries and replay it as
// If-Match on the matching PUT/DELETE, so protected edits are checked against the version the
// user actually holds. Keyed by resource path. Nothing is sent when no version was captured
// (last-write-wins).
const etags = new Map<string, string>();

function resourcePath(url: string): string {
  try {
    return new URL(url, window.location.origin).pathname;
  } catch {
    return url;
  }
}

/**
 * Which resource an answer's entity tag belongs to, or nothing when the answer does not say.
 *
 * A read or a full update answers with the resource itself, so the tag is the request path's. A
 * creation answers with the new resource, which Location names. An action posted beside a
 * resource — a trip's state move, say — answers with the version of the resource it moved and
 * names it in Content-Location; without that header the tag is nobody's and is not kept, because
 * filing it under the action's own path would guard nothing and filing it under a guessed path
 * could guard the wrong thing.
 */
function versionedResource(request: Request, response: Response): string | undefined {
  const named = response.headers.get('Content-Location');
  if (named) {
    return resourcePath(named);
  }
  if (response.status === 201) {
    const created = response.headers.get('Location');
    return created ? resourcePath(created) : undefined;
  }
  if (request.method === 'GET' || request.method === 'PUT') {
    return resourcePath(request.url);
  }
  return undefined;
}

/**
 * Files the version each answer carries under the resource it is of, and replays it on the next
 * full update or delete of that resource. A write's answer counts exactly as a read's does: the
 * version a write produced is what the next write must carry, and a client that learned versions
 * only from reads was one behind after every save, so a second save a moment later was refused as
 * a conflict nobody could see.
 */
export const threadsVersions = {
  onRequest({ request }: { request: Request }) {
    if (request.method === 'PUT' || request.method === 'DELETE') {
      const etag = etags.get(resourcePath(request.url));
      if (etag) {
        request.headers.set('If-Match', etag);
      }
    }
    return request;
  },
  onResponse({ request, response }: { request: Request; response: Response }) {
    const etag = response.headers.get('ETag');
    if (etag) {
      const resource = versionedResource(request, response);
      if (resource) {
        etags.set(resource, etag);
      }
    }
    return response;
  },
};

api.use(threadsVersions);

/**
 * The version last read for a resource, for a write the replay above cannot thread by itself.
 *
 * An action posted to a sub-path of a resource — announcing a trip, say — is a write on that
 * resource and the server checks the precondition exactly as it does on a full update, but the
 * request's own path is not the one the ETag was captured under and its method is not one that
 * carries a precondition by default. Callers in that position ask for the version here and set
 * the header themselves, so the opt-in stays visible at the call site: replaying preconditions
 * onto every POST would start refusing action endpoints that never asked for one.
 */
export function lastReadETag(path: string): string | undefined {
  return etags.get(path);
}

// What the application asked the server, kept as part of the trail attached to an error report.
// Development only, and only what a request line would say — method, path, status. It is here rather
// than around each call because this is the one place every request already passes through, and the
// answer a request got is very often what explains the error that follows it.
if (import.meta.env.DEV) {
  api.use({
    onResponse({ request, response }) {
      recordBreadcrumb('api', `${request.method} ${new URL(request.url).pathname} → ${response.status}`);
      return response;
    },
  });
}

/**
 * One GET, made the way the generated client makes one, for a route the contract document does not
 * describe yet.
 *
 * <p>
 * The typed client is built from the contract the server publishes, so a route added in the same
 * change as the screen reading it cannot be reached through that client until the document has been
 * generated again. This is the way through meanwhile — and it goes through the same steps rather
 * than around them, which is the whole reason it lives here beside them rather than beside its
 * callers.
 * </p>
 * <p>
 * Three of those steps matter and each is a defect if it is skipped. The account's bearer, or the
 * request is refused. The language the person is reading in, or a sentence the server wrote comes
 * back in a language they did not choose — and these routes have such sentences, because what a
 * neighbouring library said is the server's to word. And the trail of requests kept in development,
 * without which an error report is silent about exactly the newest and least-proven surface.
 * </p>
 * <p>
 * Refusals arrive as the same error carrying the server's own stable code, so a screen goes on
 * choosing its wording from the code rather than from a sentence written for a person.
 * </p>
 */
export async function readJson<T>(path: string): Promise<T> {
  const request = new Request(new URL(path, window.location.origin), {
    headers: { Accept: 'application/json' },
  });

  await carriesTheAccountsBearer.onRequest({ request });
  sendsTheReadingLanguage.onRequest({ request });

  const response = await fetch(request);

  if (import.meta.env.DEV) {
    recordBreadcrumb(
      'api',
      `${request.method} ${new URL(request.url).pathname} → ${response.status}`,
    );
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => undefined)) as
      | Record<string, unknown>
      | undefined;

    throw new ApiError(
      response.status,
      typeof problem?.code === 'string' ? problem.code : undefined,
      typeof problem?.detail === 'string' ? problem.detail : undefined,
      problem,
      retryAfterOf(response),
    );
  }

  return (await response.json()) as T;
}
