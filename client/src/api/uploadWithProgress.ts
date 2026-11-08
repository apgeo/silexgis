// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';
import { recordBreadcrumb } from '../diagnostics/breadcrumbs.ts';
import i18n from '../i18n';
import { ApiError, retryAfterOf } from './client.ts';

/** How much of a file has gone up so far. */
export interface UploadProgress {
  loaded: number;
  total: number;
}

/**
 * Sends a multipart form to the API and reports how much of it has gone up, which the typed
 * client cannot: `fetch` says nothing about a request body while it is being sent, and a large
 * map sheet takes minutes on a slow line with nothing on screen to say it is moving. So this one
 * request is made the older way, with the same account and language headers the typed client puts
 * on every call.
 *
 * Resolves with the answer's body. A refusal is thrown as the same {@link ApiError} the typed
 * client throws, with the problem's code and detail when the server wrote one; a request that
 * never got an answer (the connection dropped, the browser went offline) is thrown with status 0;
 * one stopped through `signal` rejects with an `AbortError`.
 */
export async function uploadWithProgress<T>(
  path: string,
  form: FormData,
  { onProgress, signal }: { onProgress?: (progress: UploadProgress) => void; signal?: AbortSignal } = {},
): Promise<T> {
  const user = await userManager.getUser();
  return new Promise<T>((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('POST', path);
    if (user?.access_token) {
      request.setRequestHeader('Authorization', `Bearer ${user.access_token}`);
    }
    const language = i18n.resolvedLanguage ?? i18n.language;
    if (language) {
      request.setRequestHeader('Accept-Language', language);
    }
    request.setRequestHeader('Accept', 'application/json');

    request.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress?.({ loaded: event.loaded, total: event.total });
      }
    };
    request.onload = () => {
      recordBreadcrumb('api', `POST ${path} → ${request.status}`);
      const body = parseJson(request.responseText);
      if (request.status >= 200 && request.status < 300) {
        resolve(body as T);
        return;
      }
      const problem = body && typeof body === 'object' ? (body as Record<string, unknown>) : undefined;
      reject(
        new ApiError(
          request.status,
          typeof problem?.code === 'string' ? problem.code : undefined,
          typeof problem?.detail === 'string' ? problem.detail : undefined,
          problem,
          retryAfterOf({ headers: { get: (name: string) => request.getResponseHeader(name) } }),
        ),
      );
    };
    request.onerror = () => reject(new ApiError(0));
    request.ontimeout = () => reject(new ApiError(0));
    request.onabort = () => reject(new DOMException('The upload was stopped.', 'AbortError'));
    if (signal) {
      if (signal.aborted) {
        request.abort();
        return;
      }
      signal.addEventListener('abort', () => request.abort(), { once: true });
    }
    request.send(form);
  });
}

function parseJson(text: string): unknown {
  if (!text) {
    return undefined;
  }
  try {
    return JSON.parse(text);
  } catch {
    // A proxy's HTML page (a body over its size limit, a gateway error) is not a problem object.
    return undefined;
  }
}
