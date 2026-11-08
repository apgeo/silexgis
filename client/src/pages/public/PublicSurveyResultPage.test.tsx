// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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

const signIn = vi.fn(async () => {});
vi.mock('../../auth/auth.tsx', () => ({ useAuth: () => ({ signIn }) }));

const downloadFile = vi.fn(async (_url: string) => {});
vi.mock('../../api/download.ts', () => ({ downloadFile: (url: string) => downloadFile(url) }));

const { default: PublicSurveyResultPage } = await import('./PublicSurveyResultPage.tsx');

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/p/survey/abc']}>
        <Routes>
          <Route path="/p/survey/:token" element={<PublicSurveyResultPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

afterEach(() => {
  cleanup();
  view = undefined;
  failure = undefined;
  signIn.mockClear();
  downloadFile.mockReset().mockResolvedValue(undefined);
});

function failedCompile(over: Partial<PublicResultView> = {}): PublicResultView {
  return {
    title: 'Invented survey',
    caveName: 'Invented cave',
    version: '2026-10-25T10:15:00Z',
    language: null,
    status: 'failed',
    progress: 60,
    message: null,
    error: { code: 'survey_pipeline.compile_failed', message: null },
    stages: [
      { stage: 'archive', state: 'done' },
      { stage: 'compile', state: 'failed' },
    ],
    artefacts: [],
    logTail: 'error: invented failure -- an invented reason',
    log: { name: 'invented_2026-10-25_101500.log', sizeBytes: 2048, url: '/api/v1/public/survey-uploads/abc/log' },
    ...over,
  };
}

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
      logTail: null,
      log: null,
      language: null,
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
      language: null,
      version: '2026-10-25T10:15:00Z',
      status: 'succeeded',
      stage: 'unpack',
      stages: [],
      progress: 100,
      message: null,
      error: null,
      artefacts: [],
      createdAt: '2026-10-25T10:15:00Z',
      finishedAt: null,
      logTail: null,
      log: null,
      // Not part of the envelope type: what a wrong server might add.
      team: 'Invented Team Member',
      files: [{ name: 'invented-source.th' }],
    } as Parameters<typeof publicResultView>[0]);
    expect(JSON.stringify(view)).not.toContain('Invented Team Member');
    expect(JSON.stringify(view)).not.toContain('invented-source.th');
  });

  it('shows a failed compile its log underneath, as the sentence says, and saves the whole log when asked', async () => {
    view = failedCompile();
    show();
    expect(screen.getByTestId('survey-upload-error').textContent).toContain('The log below says where.');
    expect(screen.getByTestId('public-survey-log-tail').textContent).toBe('error: invented failure -- an invented reason');
    expect(screen.getByTestId('public-survey-log').textContent).toContain('invented_2026-10-25_101500.log');
    fireEvent.click(screen.getByTestId('public-survey-log-download'));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith('/api/v1/public/survey-uploads/abc/log'));
  });

  it('says so when the whole log could not be saved', async () => {
    downloadFile.mockRejectedValue(new Error('refused'));
    view = failedCompile();
    show();
    fireEvent.click(screen.getByTestId('public-survey-log-download'));
    expect(await screen.findByText('The log could not be downloaded. Please try again.')).toBeTruthy();
  });

  it('shows the log of a run that succeeded too', () => {
    view = failedCompile({ status: 'succeeded', progress: 100, error: null, logTail: 'compilation time: 1 sec' });
    show();
    expect(screen.getByTestId('public-survey-log-tail').textContent).toBe('compilation time: 1 sec');
    expect(screen.getByTestId('public-survey-log-download')).toBeTruthy();
  });

  it('points to no log underneath when the run left none to show', () => {
    view = failedCompile({ logTail: null, log: null });
    show();
    expect(screen.getByTestId('survey-upload-error').textContent).toContain('Therion could not compile the survey.');
    expect(screen.getByTestId('survey-upload-error').textContent).not.toContain('below');
    expect(screen.queryByTestId('public-survey-log')).toBeNull();
  });

  it('offers the page of results once the run has ended, and not while it runs', () => {
    view = failedCompile({ status: 'succeeded', progress: 100, error: null });
    show();
    expect(screen.getByTestId('public-survey-open-viewer')).toHaveAttribute('href', '/p/survey/abc/view');
    cleanup();

    view = failedCompile({ status: 'running', progress: 40, error: null });
    show();
    expect(screen.queryByTestId('public-survey-open-viewer')).toBeNull();
  });

  it('says the link does not work, without a retry, when the server knows no such link', () => {
    failure = new ApiError(404, 'survey_upload.not_found');
    show();
    expect(screen.getByTestId('public-survey-unavailable')).toBeTruthy();
    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
  });

  it('asks a reader of a link made for the organization to sign in, and brings them back here', () => {
    failure = new ApiError(401, 'survey_upload.public_link_sign_in_required');
    show();
    expect(screen.getByTestId('public-survey-sign-in')).toBeTruthy();
    expect(screen.queryByTestId('public-survey-unavailable')).toBeNull();
    expect(screen.queryByRole('button', { name: /retry/i })).toBeNull();
    fireEvent.click(screen.getByTestId('public-survey-sign-in-button'));
    expect(signIn).toHaveBeenCalledWith('/p/survey/abc');
  });

  it('does not take any other 401 for the organization prompt', () => {
    failure = new ApiError(401, 'auth.something_else');
    show();
    expect(screen.queryByTestId('public-survey-sign-in')).toBeNull();
    expect(screen.getByTestId('public-survey-unavailable')).toBeTruthy();
  });

  it('offers a retry when the failure is not a refusal', () => {
    failure = new ApiError(500);
    show();
    expect(screen.getByRole('button', { name: /retry/i })).toBeTruthy();
  });
});
