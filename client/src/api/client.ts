// SPDX-License-Identifier: AGPL-3.0-or-later
import createClient from 'openapi-fetch';
import type { paths } from './schema';
import { userManager } from '../auth/auth.tsx';

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
