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

  constructor(status: number, code?: string, detail?: string, problem?: Record<string, unknown>) {
    super(`API error ${status}${code ? ` (${code})` : ''}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.problem = problem;
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
 * How often a failed read is attempted again. A 4xx is the server's settled answer — the
 * identical request cannot produce anything else, so the three default attempts merely hold
 * the screen in its loading state for the whole 1s + 2s + 4s backoff before the failure
 * finally becomes visible. Rate limiting is the one client error worth waiting out: 429
 * clears by itself once the window rolls over. Network faults and 5xx keep the retries.
 */
export function retryQuery(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status >= 400 && error.status < 500 && error.status !== 429) {
    return false;
  }
  return failureCount < 3;
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

// Optimistic-concurrency threading: remember the ETag from each single-resource GET and
// replay it as If-Match on the matching PUT/DELETE, so protected edits are checked against
// the version the user actually loaded. Keyed by resource path — the GET and the write share
// the same URL. Nothing is sent when no version was captured (last-write-wins).
const etags = new Map<string, string>();

function resourcePath(url: string): string {
  try {
    return new URL(url, window.location.origin).pathname;
  } catch {
    return url;
  }
}

api.use({
  onRequest({ request }) {
    if (request.method === 'PUT' || request.method === 'DELETE') {
      const etag = etags.get(resourcePath(request.url));
      if (etag) {
        request.headers.set('If-Match', etag);
      }
    }
    return request;
  },
  onResponse({ request, response }) {
    if (request.method === 'GET') {
      const etag = response.headers.get('ETag');
      if (etag) {
        etags.set(resourcePath(request.url), etag);
      }
    }
    return response;
  },
});

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
    );
  }

  return (await response.json()) as T;
}
