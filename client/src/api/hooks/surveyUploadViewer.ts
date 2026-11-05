// SPDX-License-Identifier: AGPL-3.0-or-later
//
// The page of one upload's results: the compiler's figures and the upload's own survey measured,
// the survey's line work placed in the world, the model the viewers draw, and the addresses the
// page is shared and reached by.
//
// Both the signed-in page and a public link's page ask for the same things, each on its own route:
// the signed-in one by the upload's id, the public one by the link's token. What either may be
// shown is the server's decision, not this file's.
import { useQuery } from '@tanstack/react-query';
import { api, ApiError, readJson } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { retryUnlessRefused, unwrap } from './shared.ts';
import type { SurveyModelInfo } from './surveys.ts';
import type { SurveyUploadArtefactView } from './surveyUploadPublicLinks.ts';
import { surveyUploadLinkHref } from './surveyUploads.ts';

export type SurveyUploadFigures = components['schemas']['SurveyUploadFiguresDto'];
export type SurveyLogFigures = components['schemas']['SurveyLogFiguresDto'];
export type SurveyLogLoop = components['schemas']['SurveyLogLoopDto'];
export type SurveyLogExtent = components['schemas']['SurveyLogExtentDto'];
export type SurveyUploadAnalyses = components['schemas']['SurveyUploadAnalysesDto'];

/** The formats the line work is offered in, in the order the page lists them. */
export const SURVEY_GEODATA_FORMATS = ['kml', 'geojson', 'gpx'] as const;
export type SurveyGeodataFormat = (typeof SURVEY_GEODATA_FORMATS)[number];

/** The server's answer for a run that placed no line in the world (or one withheld as if it had not). */
export const NOT_GEOREFERENCED = 'survey_upload.not_georeferenced';

/** The line work as GeoJSON: a feature collection in WGS 84. */
export interface SurveyLineWork {
  type: 'FeatureCollection';
  features: unknown[];
}

/** The page of results for an upload, from its own page's address. */
export function surveyUploadViewerPath(statusPageUrl: string): string {
  return `${statusPageUrl.replace(/\/+$/, '')}/view`;
}

/** The page of results behind a public link, from the link's own address (`/p/survey/<token>`). */
export function publicSurveyViewerPath(publicLinkUrl: string): string {
  return `${publicLinkUrl.replace(/\/+$/, '')}/view`;
}

/** Where the line work downloads from, in one format. */
export function surveyUploadGeodataUrl(id: string, format: SurveyGeodataFormat): string {
  return `/api/v1/survey-uploads/${encodeURIComponent(id)}/geodata?format=${format}`;
}

/** Where the line work behind a public link downloads from, in one format. */
export function publicSurveyUploadGeodataUrl(token: string, format: SurveyGeodataFormat): string {
  return `/api/v1/public/survey-uploads/${encodeURIComponent(token)}/geodata?format=${format}`;
}

/** What the two survey viewers need of a model: where its file is, a name and which format it is. */
export type ViewerModel = Pick<SurveyModelInfo, 'modelUrl' | 'name' | 'format'>;

/**
 * The model a run produced, for the viewers: its compiled 3D file, else its Survex file — the
 * same survey either way, and the first is the richer drawing. Null when the run produced neither
 * with an address to fetch it from.
 *
 * The name loses its extension because the viewers are handed `name.format` and choose their parser
 * by what follows the dot.
 */
export function viewerModelOf(artefacts: readonly SurveyUploadArtefactView[]): ViewerModel | null {
  const pick = (kind: string) => artefacts.find((a) => a.kind === kind && a.contentUrl);
  const lox = pick('lox');
  const chosen = lox ?? pick('3d');
  if (!chosen?.contentUrl) {
    return null;
  }
  return {
    modelUrl: chosen.contentUrl,
    name: chosen.name.replace(/\.[^./]+$/, '') || 'survey',
    format: lox ? 'lox' : 'survex3d',
  };
}

/**
 * The run's figures. Asked for once the run has finished, like the line work: a run still going
 * has no log to read yet.
 */
export function useSurveyUploadFigures(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.surveyUploadFigures(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/survey-uploads/{id}/figures', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    retry: retryUnlessRefused,
  });
}

export function usePublicSurveyUploadFigures(token: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.publicSurveyUploadFigures(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/survey-uploads/{token}/figures', { params: { path: { token: token! } } })),
    enabled: !!token && enabled,
    retry: retryUnlessRefused,
  });
}

/**
 * The upload's line work as GeoJSON, or null when the run placed none. Asked for only once the
 * caller knows the run has finished: a run still going has written no line yet, and asking would
 * cache an answer the finished run contradicts.
 */
export function useSurveyUploadLineWork(id: string | undefined, enabled: boolean) {
  return useLineWork(queryKeys.surveyUploadGeodata(id ?? ''), () => surveyUploadGeodataUrl(id!, 'geojson'), !!id && enabled);
}

/** The same through a public link: the line the upload's own files place. */
export function usePublicSurveyUploadLineWork(token: string | undefined, enabled: boolean) {
  return useLineWork(
    queryKeys.publicSurveyUploadGeodata(token ?? ''),
    () => publicSurveyUploadGeodataUrl(token!, 'geojson'),
    !!token && enabled,
  );
}

function useLineWork(queryKey: readonly unknown[], url: () => string, enabled: boolean) {
  return useQuery({
    queryKey,
    queryFn: async (): Promise<SurveyLineWork | null> => {
      try {
        return await readJson<SurveyLineWork>(url());
      } catch (error) {
        if (error instanceof ApiError && error.status === 404 && error.code === NOT_GEOREFERENCED) {
          return null;
        }
        throw error;
      }
    },
    enabled,
    retry: retryUnlessRefused,
  });
}

/**
 * Where a signed-in reader of a public link finds the same upload in the application — or null when
 * the upload does not answer them, which the server says as it says an invented link.
 */
export function useSurveyUploadByPublicLink(token: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.surveyUploadByPublicLink(token ?? ''),
    queryFn: async () => {
      try {
        return await unwrap(
          api.GET('/api/v1/survey-uploads/by-public-link/{token}', { params: { path: { token: token! } } }),
        );
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          return null;
        }
        throw error;
      }
    },
    enabled: !!token && enabled,
    retry: retryUnlessRefused,
  });
}

/**
 * The address "Copy link" copies: the public link's page when the upload has one — the server
 * shows the link only to readers the upload answers — else this page's own address.
 */
export function surveyResultShare(
  upload: { publicLink: { url: string } | null; statusPageUrl: string },
  origin: string,
): { url: string; kind: 'public' | 'inApp' } {
  return upload.publicLink
    ? { url: surveyUploadLinkHref(publicSurveyViewerPath(upload.publicLink.url), origin), kind: 'public' }
    : { url: surveyUploadLinkHref(surveyUploadViewerPath(upload.statusPageUrl), origin), kind: 'inApp' };
}
