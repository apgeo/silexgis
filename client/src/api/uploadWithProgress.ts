// SPDX-License-Identifier: AGPL-3.0-or-later
import { userManager } from '../auth/auth.tsx';
import { recordBreadcrumb } from '../diagnostics/breadcrumbs.ts';
import i18n from '../i18n';
import { ApiError, renewingALapsedSignInOnce, retryAfterOf } from './client.ts';

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
 * on every call — and the same renewal: refused because the sign-in ran out while the page was
 * open, the sign-in is renewed once and the form sent again from the start with the new bearer.
 *
 * Resolves with the answer's body. A refusal is thrown as the same {@link ApiError} the typed
 * client throws, with the problem's code and detail when the server wrote one; a request that
 * never got an answer (the connection dropped, the browser went offline) is thrown with status 0;
 * one stopped through `signal` rejects with an `AbortError`.
 */
export async function uploadWithProgress<T>(
  path: string,
  form: FormData,
  options: { onProgress?: (progress: UploadProgress) => void; signal?: AbortSignal } = {},
): Promise<T> {
  const user = await userManager.getUser();
  const bearer = user?.access_token ? `Bearer ${user.access_token}` : undefined;
  const answer = bearer
    ? await renewingALapsedSignInOnce((renewed) => sendOnce(path, form, renewed ?? bearer, options))
    : await sendOnce(path, form, undefined, options);

  if (answer.status >= 200 && answer.status < 300) {
    return answer.body as T;
  }
  const problem =
    answer.body && typeof answer.body === 'object' ? (answer.body as Record<string, unknown>) : undefined;
  throw new ApiError(
    answer.status,
    typeof problem?.code === 'string' ? problem.code : undefined,
    typeof problem?.detail === 'string' ? problem.detail : undefined,
    problem,
    answer.retryAfterMs,
  );
}

/** What the server answered one send with: its status, its body, and any wait it asked for. */
interface Answer {
  status: number;
  body: unknown;
  retryAfterMs?: number;
}

/**
 * Sends the form once. Resolves with whatever the server answered, refusals included — whether a
 * refusal is the last word is decided above — and rejects only when there was no answer to have.
 */
function sendOnce(
  path: string,
  form: FormData,
  authorization: string | undefined,
  { onProgress, signal }: { onProgress?: (progress: UploadProgress) => void; signal?: AbortSignal },
): Promise<Answer> {
  return new Promise<Answer>((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('POST', path);
    if (authorization) {
      request.setRequestHeader('Authorization', authorization);
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
      resolve({
        status: request.status,
        body: parseJson(request.responseText),
        retryAfterMs: retryAfterOf({ headers: { get: (name: string) => request.getResponseHeader(name) } }),
      });
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
