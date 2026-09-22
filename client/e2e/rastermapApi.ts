// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect, type Page } from '@playwright/test';
import { uniquePng } from './png.ts';

/**
 * API plumbing the raster-map specs share: the signed-in page's own bearer token, JSON
 * calls spending it, and the uploads and folds the authoring flows are verified against.
 * The UI is driven by the specs; this file only reads back what the UI wrote and seeds
 * what a spec does not itself author.
 */

/**
 * The signed-in page's own bearer token, read from the same in-memory session the SPA
 * spends. Vite serves the auth module at its source path and the browser's module cache
 * returns the instance the app itself built, so this is the session, not a second login.
 */
export async function bearerToken(page: Page): Promise<string> {
  return page.evaluate<string>(`import('/src/auth/auth.tsx').then(async (m) => {
    const user = await m.userManager.getUser();
    if (!user) throw new Error('no signed-in session in the page');
    return user.access_token;
  })`);
}

export async function apiJson(
  page: Page,
  token: string,
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  data?: unknown,
  headers: Record<string, string> = {},
): Promise<unknown> {
  const response = await page.request.fetch(path, {
    method,
    headers: { Authorization: `Bearer ${token}`, ...headers },
    ...(data === undefined ? {} : { data }),
  });
  expect(response.ok(), `${method} ${path} answered ${response.status()}`).toBeTruthy();
  return response.status() === 204 ? undefined : response.json();
}

/** Uploads a fresh noise PNG — never a real map — answering the file and its document. */
export async function uploadMapPng(
  page: Page,
  token: string,
  name: string,
  size = 512,
): Promise<{ id: string; documentId: string }> {
  const upload = await page.request.post('/api/v1/files?allowDuplicate=false', {
    headers: { Authorization: `Bearer ${token}` },
    multipart: { file: { name, mimeType: 'image/png', buffer: uniquePng(size) } },
  });
  expect(upload.ok(), `upload answered ${upload.status()}`).toBeTruthy();
  return upload.json() as Promise<{ id: string; documentId: string }>;
}

/** Uploads a new scan of the same document, superseding the file the pins were measured on. */
export async function uploadMapVersion(
  page: Page,
  token: string,
  fileId: string,
  name: string,
  size = 512,
): Promise<{ id: string }> {
  const upload = await page.request.post(`/api/v1/files/${fileId}/versions`, {
    headers: { Authorization: `Bearer ${token}` },
    multipart: { file: { name, mimeType: 'image/png', buffer: uniquePng(size) } },
  });
  expect(upload.ok(), `version upload answered ${upload.status()}`).toBeTruthy();
  return upload.json() as Promise<{ id: string }>;
}

export const relationId = (
  relationTypes: { id: number; code: string }[],
  code: string,
): number => {
  const row = relationTypes.find((r) => r.code === code);
  expect(row, `seeded relation ${code} is missing`).toBeTruthy();
  return row!.id;
};

interface LinkMember {
  targetType: string;
  targetId: string;
  anchorKind: string;
  anchor: { station?: string; x?: number; y?: number; shape?: string } | null;
  anchorFileId: string | null;
}

export interface PinLink {
  id: string;
  relationType: { code: string } | null;
  members: LinkMember[];
}

/** Every station-point link of one station on one map document, straight off the API. */
export async function pinsOf(
  page: Page,
  token: string,
  modelId: string,
  documentId: string,
  station: string,
): Promise<PinLink[]> {
  const answer = (await apiJson(
    page,
    token,
    'GET',
    `/api/v1/reslinks/for-target?type=surveyModel&id=${modelId}&pageSize=200`,
  )) as { items: PinLink[] };
  return answer.items.filter(
    (link) =>
      link.relationType?.code === 'map-station-point'
      && link.members.some(
        (member) => member.anchorKind === 'modelStation' && member.anchor?.station === station,
      )
      && link.members.some(
        (member) => member.targetType === 'document' && member.targetId === documentId,
      ),
  );
}

/** The document member of a pin link — the fractions and the measured-against file. */
export function pointMemberOf(link: PinLink): LinkMember {
  const member = link.members.find((m) => m.anchorKind === 'imageRegion');
  expect(member, `link ${link.id} has no image-region member`).toBeTruthy();
  return member!;
}

/**
 * Waits until the station holds exactly `count` pin links on the map document, answering
 * them — the write-side truth every UI act in these specs is verified against.
 */
export async function expectPinCount(
  page: Page,
  token: string,
  modelId: string,
  documentId: string,
  station: string,
  count: number,
): Promise<PinLink[]> {
  let last: PinLink[] = [];
  await expect
    .poll(
      async () => {
        last = await pinsOf(page, token, modelId, documentId, station);
        return last.length;
      },
      { timeout: 15_000 },
    )
    .toBe(count);
  return last;
}
