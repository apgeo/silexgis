// SPDX-License-Identifier: AGPL-3.0-or-later
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, type Page } from '@playwright/test';
import { asPerson } from './arrange.ts';

/**
 * An invented TopoDroid archive, read from the server's own test fixtures rather than copied here:
 * two copies of a binary file drift into two different surveys. It names no real cave, person or
 * place.
 */
const archivePath = path.join(
  path.dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  'server',
  'tests',
  'SilexGis.Api.Tests',
  'Fixtures',
  'topodroid',
  'pestera_inventata.zip',
);

export const archiveName = 'pestera_inventata.zip';

export interface SeededUpload {
  id: string;
  caveId: string;
  caveName: string;
  caveSurveyId: string;
  statusPageUrl: string;
  status: string;
  publicLinkUrl: string | null;
}

interface UploadAnswer {
  id: string;
  status: string;
  cave: { id: string; name: string };
  caveSurvey: { id: string };
  statusPageUrl: string;
  publicLink: { url: string } | null;
}

/**
 * Sends an upload the way a device does — open, one PUT per file, complete — as the person signed
 * in on `page`, and waits until the run has ended. No cave is named, so the server makes one from
 * the title, which is what a first upload from a phone does.
 *
 * Seeding through the API, not the screens, because the web has no way to start an upload: the
 * pages under test only show one.
 */
export async function seedUpload(page: Page, title: string): Promise<SeededUpload> {
  const bytes = fs.readFileSync(archivePath);
  const opened = await asPerson<UploadAnswer>(page, 'POST', '/api/v1/survey-uploads', {
    source: 'topodroid',
    client: { name: 'TopoDroid', version: '6.5.34' },
    surveyTitle: title,
    surveyName: 'pestera_inventata',
    files: [{ name: archiveName, size: bytes.length, role: 'archive' }],
    pipeline: { compile: true },
  });
  await asPerson(page, 'PUT', `/api/v1/survey-uploads/${opened.id}/files/${encodeURIComponent(archiveName)}`, bytes, {
    'Content-Type': 'application/octet-stream',
  });
  await asPerson(page, 'POST', `/api/v1/survey-uploads/${opened.id}/complete`);

  let last = opened;
  await expect
    .poll(
      async () => {
        last = await asPerson<UploadAnswer>(page, 'GET', `/api/v1/survey-uploads/${opened.id}`);
        return last.status;
      },
      { timeout: 90_000, message: 'the seeded upload never ended its run' },
    )
    .toMatch(/^(succeeded|failed)$/);

  return {
    id: last.id,
    caveId: last.cave.id,
    caveName: last.cave.name,
    caveSurveyId: last.caveSurvey.id,
    statusPageUrl: last.statusPageUrl,
    status: last.status,
    publicLinkUrl: last.publicLink?.url ?? null,
  };
}
