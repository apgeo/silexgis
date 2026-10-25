// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import { publicResultView, type PublicResultView } from '../../api/hooks.ts';

/** The page behind a public link: the same progress view as the signed-in page, and one sentence for every dead link. */

let view: PublicResultView | undefined;
let failure: unknown;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    usePublicSurveyUpload: () => ({ data: view, error: failure, isPending: !view && !failure, refetch: vi.fn() }),
  };
});

const { default: PublicSurveyResultPage } = await import('./PublicSurveyResultPage.tsx');

function show() {
  return render(
    <MemoryRouter initialEntries={['/p/survey/abc']}>
      <Routes>
        <Route path="/p/survey/:token" element={<PublicSurveyResultPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  view = undefined;
  failure = undefined;
});

describe('PublicSurveyResultPage', () => {
  it('draws the title, the cave name, the stages and the files that can be opened', () => {
    view = {
      title: 'Invented survey',
      caveName: 'Invented cave',
      version: '2026-10-25T10:15:00Z',
      status: 'succeeded',
      progress: 100,
      message: null,
      error: null,
      stages: [
        { stage: 'archive', state: 'done' },
        { stage: 'unpack', state: 'done' },
      ],
      artefacts: [{ kind: 'plan-pdf', name: 'plan.pdf', sizeBytes: 1024, contentUrl: '/files/plan' }],
    };
    show();
    expect(screen.getByText('Invented survey')).toBeTruthy();
    expect(screen.getByTestId('public-survey-cave').textContent).toContain('Invented cave');
    expect(screen.getByTestId('survey-upload-stage-archive')).toBeTruthy();
    expect(screen.getByText('plan.pdf')).toBeTruthy();
  });

  it('keeps members a public answer must not hold out of the view, even if the server sent them', () => {
    const view = publicResultView({
      survey: { title: 'Invented survey' },
      cave: { name: 'Invented cave' },
      version: '2026-10-25T10:15:00Z',
      status: 'succeeded',
      stage: 'unpack',
      progress: 100,
      message: null,
      error: null,
      artefacts: [],
      createdAt: '2026-10-25T10:15:00Z',
      finishedAt: null,
      // Not part of the envelope type: what a wrong server might add.
      logTail: 'invented-log-line',
      team: 'Invented Team Member',
      files: [{ name: 'invented-source.th' }],
    } as Parameters<typeof publicResultView>[0]);
    expect(JSON.stringify(view)).not.toContain('invented-log-line');
    expect(JSON.stringify(view)).not.toContain('Invented Team Member');
    expect(JSON.stringify(view)).not.toContain('invented-source.th');
  });

  it('says the link does not work, without a retry, when the server knows no such link', () => {
    failure = new ApiError(404, 'survey_upload.not_found');
    show();
    expect(screen.getByTestId('public-survey-unavailable')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
  });

  it('offers a retry when the failure is not a refusal', () => {
    failure = new ApiError(500);
    show();
    expect(screen.getByRole('button', { name: /retry/i })).toBeTruthy();
  });
});
