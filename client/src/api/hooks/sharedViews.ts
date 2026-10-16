// SPDX-License-Identifier: AGPL-3.0-or-later
import { api, ApiError, retryAfterOf } from '../client.ts';
import type { components } from '../schema';

/** A shared map view as its link opens it: the document, a keyless background and its overlays. */
export type SharedView = components['schemas']['SharedViewDto'];

/** The background a shared view's page draws — always one that needs no access key. */
export type SharedViewBackground = components['schemas']['SharedViewBackgroundDto'];

type FeatureCollection = components['schemas']['FeatureCollection'];

async function read<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    throw new ApiError(
      response.status,
      problem?.code,
      problem?.detail,
      problem as Record<string, unknown> | undefined,
      retryAfterOf(response),
    );
  }
  return data;
}

/**
 * The public cave entrances a shared view's link draws in a window: clusters below the
 * installation's cluster zoom, points from it up, every protected position snapped. Anonymous.
 */
export function fetchSharedViewEntrances(token: string, bbox: string, zoom: number): Promise<FeatureCollection> {
  return read(
    api.GET('/api/v1/shared/views/{token}/cave-entrances', {
      params: { path: { token }, query: { bbox, zoom } },
    }),
  );
}

/** The public surface features a shared view's link draws in a window. Anonymous. */
export function fetchSharedViewFeatures(token: string, bbox: string): Promise<FeatureCollection> {
  return read(
    api.GET('/api/v1/shared/views/{token}/features', { params: { path: { token }, query: { bbox } } }),
  );
}
