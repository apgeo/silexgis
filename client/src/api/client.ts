// SPDX-License-Identifier: AGPL-3.0-or-later
import createClient from 'openapi-fetch';
import type { paths } from './schema';
import { userManager } from '../auth/auth.tsx';
import { recordBreadcrumb } from '../diagnostics/breadcrumbs.ts';

/** Thrown by the unwrap helpers so callers can react to the HTTP status / problem code. */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(status: number, code?: string) {
    super(`API error ${status}${code ? ` (${code})` : ''}`);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
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

api.use({
  async onRequest({ request }) {
    const user = await userManager.getUser();
    if (user?.access_token) {
      request.headers.set('Authorization', `Bearer ${user.access_token}`);
    }
    return request;
  },
});

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
