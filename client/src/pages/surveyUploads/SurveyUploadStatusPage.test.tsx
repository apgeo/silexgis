// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SurveyUpload } from '../../api/hooks.ts';

/**
 * The status page of one upload: what a reader sees, and what only someone who may write the cave
 * is offered.
 */

const rerunMutate = vi.fn();
const mintMutate = vi.fn();
const revokeMutate = vi.fn();
let upload: SurveyUpload | undefined;
let uploadError: unknown;
let canWrite: boolean;
let groups: { id: string; versions: { id: string; version: string; status: string; statusPageUrl: string }[] }[];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveyUpload: () => ({ data: upload, error: uploadError, isPending: !upload && !uploadError, refetch: vi.fn() }),
    useCaveSurveys: () => ({ data: groups }),
    useRefreshCaveWhenUploadSettles: () => undefined,
    useCaveSummary: () => ({ data: { permissions: { canWrite } } }),
    useStorageRoom: () => ({ data: undefined }),
    useRerunSurveyUpload: () => ({ mutateAsync: rerunMutate, isPending: false }),
    // The scan card's "make a surface" asks for the cave's models to know whether the scan is placed.
    useSurveyModels: () => ({ data: [], isPending: false }),
    useMakeScanSurface: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useMintSurveyUploadPublicLink: () => ({ mutateAsync: mintMutate, isPending: false }),
    useRevokeSurveyUploadPublicLink: () => ({ mutateAsync: revokeMutate, isPending: false }),
    useSurveyUploadPreferences: () => ({
      data: { alwaysOutputs: [], publicLinks: true, defaultLinkAudience: 'organization' },
    }),
  };
});

const { default: SurveyUploadStatusPage } = await import('./SurveyUploadStatusPage.tsx');

function made(over: Partial<SurveyUpload> = {}): SurveyUpload {
  return {
    id: 'u1',
    status: 'succeeded',
    stage: 'unpack',
    progress: 100,
    message: 'Archived and read, not compiled.',
    error: null,
    cave: { id: 'c1', name: 'Invented Cave', url: '/caves/c1' },
    caveSurvey: { id: 's1', title: 'Invented survey' },
    version: '2026-10-25T10:15:00Z',
    caveCreated: false,
    survey: { title: 'Invented survey', name: 'inv1', date: null, team: null },
    pointCloud: null,
    files: [
      { name: 'inv1.zip', role: 'archive', status: 'stored', declaredSize: 2048, receivedSize: 2048, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: '/files/zip' },
    ],
    artefacts: [],
    compiler: null,
    outputs: [],
    compile: false,
    logTail: 'line one of the log',
    statusPageUrl: '/caves/c1/surveys/s1/uploads/u1',
    publicLink: null,
    publicLinkRefusal: null,
    olderVersions: { count: 0, sizeBytes: 0, warn: false, warnBytes: 524288000, keepVersions: 0 },
    createdAt: '2026-10-25T10:15:00Z',
    expiresAt: '2026-10-26T10:15:00Z',
    startedAt: '2026-10-25T10:15:01Z',
    finishedAt: '2026-10-25T10:15:03Z',
    ...over,
  } as SurveyUpload;
}

function show() {
  return render(
    <App>
      <MemoryRouter initialEntries={['/caves/c1/surveys/s1/uploads/u1']}>
        <Routes>
          <Route path="/caves/:caveId/surveys/:surveyId/uploads/:uploadId" element={<SurveyUploadStatusPage />} />
        </Routes>
      </MemoryRouter>
    </App>,
  );
}

beforeEach(() => {
  groups = [];
  rerunMutate.mockReset().mockResolvedValue(undefined);
  mintMutate.mockReset().mockResolvedValue(undefined);
  revokeMutate.mockReset().mockResolvedValue(undefined);
  upload = made();
  uploadError = undefined;
  canWrite = true;
});

afterEach(cleanup);

describe('the upload status page', () => {
  it('shows the survey, the stages, the note and the input files', () => {
    show();

    expect(screen.getByRole('heading', { name: 'Invented survey' })).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-stages')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-message')).toHaveTextContent('not compiled');
    expect(screen.getByText('inv1.zip')).toBeInTheDocument();
  });

  it('shows the log tail and the run-again button to someone who may write, and neither to a reader', () => {
    show();
    expect(screen.getByTestId('survey-upload-log-tail')).toHaveTextContent('line one of the log');
    expect(screen.getByTestId('survey-upload-rerun')).toBeInTheDocument();

    cleanup();
    canWrite = false;
    upload = made({ logTail: null });
    show();
    expect(screen.queryByTestId('survey-upload-log-tail')).toBeNull();
    expect(screen.queryByTestId('survey-upload-rerun')).toBeNull();
    expect(screen.getByTestId('survey-upload-stages')).toBeInTheDocument();
  });

  it('never offers a log to a reader even if one arrived', () => {
    canWrite = false;
    upload = made({ logTail: 'leaked' });
    show();
    expect(screen.queryByTestId('survey-upload-log-tail')).toBeNull();
  });

  it('lists a failed compile’s log among the results, named, with a download that opens its address, to a writer and a reader alike', () => {
    const failed = {
      status: 'failed' as const,
      stage: 'compile' as const,
      progress: 60,
      message: null,
      error: { code: 'survey_pipeline.compile_failed', message: null },
      compile: true,
      artefacts: [
        {
          kind: 'log',
          name: 'inv1-20261025T101500Z.log',
          sizeBytes: 2048,
          contentUrl: '/api/v1/files/f-log/content?token=invented',
          surveyModelId: null,
          documentId: null,
          sourceId: 'src-log',
          viewUrl: null,
          modelStatus: null,
          error: null,
        },
      ],
    };
    for (const writer of [true, false]) {
      canWrite = writer;
      upload = made({ ...failed, logTail: writer ? 'error: invented failure' : null });
      show();
      const row = screen.getByText('inv1-20261025T101500Z.log').parentElement!;
      expect(row).toHaveTextContent("Compiler's log");
      const download = within(row).getByRole('link', { name: 'Download' });
      expect(download).toHaveAttribute('href', '/api/v1/files/f-log/content?token=invented');
      expect(download).toHaveAttribute('target', '_blank');
      // The sentence points below, where the log is.
      expect(screen.getByTestId('survey-upload-error')).toHaveTextContent('The log below says where.');
      cleanup();
    }
  });

  it('shows a point cloud with its own stages and card, no line-survey viewer, and sends the display choice as a rerun', async () => {
    upload = made({
      stage: 'downsample',
      message: 'Estimated: 12,400,000 points. Choose how much to show, then run again.',
      files: [
        { name: 'scan.laz', role: 'archive', status: 'stored', declaredSize: 4096, receivedSize: 4096, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: '/files/laz' },
      ],
      pointCloud: {
        modelId: 'm1',
        pointCount: 12_400_000,
        pointFormat: 6,
        lasVersion: '1.4',
        compressed: true,
        hasRgb: false,
        hasIntensity: true,
        sizeM: [10, 10, 2],
        georeferenced: false,
        crs: null,
        min: null,
        max: null,
        generatingSoftware: null,
        tileBytes: null,
        tileCount: null,
        tilerVersion: null,
        tilesetUrl: null,
        displayVoxelM: null,
        displayEveryNth: null,
        displayPointCount: null,
        originShift: null,
        hasCentreline: false,
        recording: null,
        estimate: {
          full: { voxelCm: null, points: 12_400_000, estimatedTileBytes: 900_000_000, estimatedSeconds: 400 },
          voxels: [{ voxelCm: 2, points: 4_000_000, estimatedTileBytes: 190_000_000, estimatedSeconds: 120 }],
        },
        advice: { level: 'warn', suggestedVoxelCm: 2, warnPoints: 5_000_000, advisePoints: 200_000_000, warnTileBytes: 2_147_483_648 },
      },
    });
    show();

    expect(screen.getByTestId('survey-upload-stage-inspect')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-stage-tile')).toBeInTheDocument();
    expect(screen.queryByTestId('survey-upload-stage-compile')).toBeNull();
    expect(screen.queryByTestId('survey-upload-open-viewer')).toBeNull();
    expect(screen.getByTestId('point-cloud-card')).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('point-cloud-choose'));
    await waitFor(() =>
      expect(rerunMutate).toHaveBeenCalledWith({ id: 'u1', display: { method: 'voxel', voxelCm: 2 } }),
    );
  });

  it('tells a point cloud before its header is read by the one file it was declared with', () => {
    upload = made({
      status: 'running',
      stage: 'archive',
      progress: 5,
      message: null,
      finishedAt: null,
      files: [
        { name: 'scan.las', role: 'archive', status: 'stored', declaredSize: 4096, receivedSize: 4096, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: null },
      ],
    });
    show();
    expect(screen.getByTestId('survey-upload-stage-downsample')).toBeInTheDocument();
    expect(screen.queryByTestId('survey-upload-stage-unpack')).toBeNull();
    expect(screen.queryByTestId('point-cloud-card')).toBeNull();
  });

  it('offers no run-again while the upload is still running, and no delete ever', () => {
    upload = made({ status: 'running', stage: 'scaffold', progress: 30, finishedAt: null, message: null });
    show();
    expect(screen.queryByTestId('survey-upload-rerun')).toBeNull();
    expect(screen.queryByRole('button', { name: /delete/i })).toBeNull();
  });

  it('runs the upload again', async () => {
    show();
    fireEvent.click(screen.getByTestId('survey-upload-rerun'));
    await waitFor(() => expect(rerunMutate).toHaveBeenCalledWith({ id: 'u1' }));
  });

  it('runs a point cloud again only after a confirmation that shows the room it needs', async () => {
    upload = made({
      status: 'failed',
      files: [
        { name: 'scan.laz', role: 'archive', status: 'stored', declaredSize: 4096, receivedSize: 4096, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: null },
      ],
    });
    show();

    fireEvent.click(screen.getByTestId('survey-upload-rerun'));
    expect(rerunMutate).not.toHaveBeenCalled();
    // The confirmation's own button, beside the one that opened it.
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Run again' })).toHaveLength(2));
    fireEvent.click(screen.getAllByRole('button', { name: 'Run again' })[1]!);
    await waitFor(() => expect(rerunMutate).toHaveBeenCalledWith({ id: 'u1' }));
  });

  it('shows a public link in full with copy, and withdraws it only after confirmation', async () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z', audience: 'public', protectedAcknowledged: false } });
    show();

    expect(screen.getByTestId('survey-upload-link')).toHaveTextContent(`${window.location.origin}/p/survey/abc`);
    expect(screen.getByRole('button', { name: /copy/i })).toBeInTheDocument();

    fireEvent.click(screen.getByTestId('survey-upload-link-revoke'));
    expect(revokeMutate).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: 'OK' }));
    await waitFor(() => expect(revokeMutate).toHaveBeenCalledWith('u1'));
  });

  it('shows the link\'s address and audience to a reader, but only a writer may replace or withdraw it', () => {
    canWrite = false;
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z', audience: 'organization', protectedAcknowledged: false } });
    show();
    expect(screen.getByTestId('survey-upload-link')).toHaveTextContent('/p/survey/abc');
    expect(screen.getByTestId('survey-upload-link-audience-tag')).toHaveTextContent('Signed-in accounts only');
    expect(screen.queryByTestId('survey-upload-link-remint')).toBeNull();
    expect(screen.queryByTestId('survey-upload-link-revoke')).toBeNull();
  });

  it('makes a link for the audience chosen in the dialog, starting from the account\'s default', async () => {
    show();
    fireEvent.click(screen.getByTestId('survey-upload-link-make'));
    expect(await screen.findByTestId('survey-upload-link-dialog')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Signed-in accounts only/ })).toBeChecked();
    expect(screen.queryByTestId('survey-upload-link-acknowledge')).toBeNull();

    fireEvent.click(screen.getByRole('radio', { name: /Anyone with the link/ }));
    fireEvent.click(screen.getByTestId('survey-upload-link-dialog-ok'));
    await waitFor(() => expect(mintMutate).toHaveBeenCalledWith({ id: 'u1', audience: 'public', allowProtected: false }));
  });

  it('replaces a link with one for the same audience unless another is chosen', async () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z', audience: 'public', protectedAcknowledged: false } });
    show();
    fireEvent.click(screen.getByTestId('survey-upload-link-remint'));
    expect(await screen.findByText(/old address stops working/)).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Anyone with the link/ })).toBeChecked();
    fireEvent.click(screen.getByTestId('survey-upload-link-dialog-ok'));
    await waitFor(() => expect(mintMutate).toHaveBeenCalledWith({ id: 'u1', audience: 'public', allowProtected: false }));
  });

  it('marks a link made knowingly for a protected cave, and asks again for the acknowledgement to replace it', async () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z', audience: 'public', protectedAcknowledged: true } });
    show();
    expect(screen.getByTestId('survey-upload-link-protected')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('survey-upload-link-remint'));
    expect(await screen.findByTestId('survey-upload-link-acknowledge')).not.toBeChecked();
    expect(screen.getByTestId('survey-upload-link-dialog-ok')).toBeDisabled();
  });

  it('draws only what the uploader who lost sight of the cave\'s position is sent', () => {
    canWrite = false;
    upload = made({
      logTail: null,
      publicLink: null,
      files: [
        { name: 'inv1.zip', role: 'archive', status: 'stored', declaredSize: 2048, receivedSize: 2048, sha256: null, refusalCode: null, sourceId: null, documentId: null, contentUrl: null },
      ],
    } as Partial<SurveyUpload>);
    show();
    expect(screen.getByTestId('survey-upload-stages')).toBeInTheDocument();
    expect(screen.getByText('inv1.zip')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /download/i })).toBeNull();
    expect(screen.queryByTestId('survey-upload-log-tail')).toBeNull();
    expect(screen.queryByTestId('survey-upload-link')).toBeNull();
  });

  it('warns when the survey\'s older versions take more than the installation warns at', () => {
    upload = made({ olderVersions: { count: 3, sizeBytes: 734003200, warn: true, warnBytes: 524288000, keepVersions: 0 } });
    show();
    const warning = screen.getByTestId('survey-upload-older-versions-warning');
    expect(warning).toHaveTextContent('versions: 3');
    expect(warning).toHaveTextContent('SILEXGIS__SurveyUploads__KeepArtefactVersions');
    expect(warning).toHaveTextContent('is 0 now');

    cleanup();
    upload = made();
    show();
    expect(screen.queryByTestId('survey-upload-older-versions-warning')).toBeNull();
  });

  it('offers to make a link when there is none, to someone who may write', () => {
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();

    cleanup();
    canWrite = false;
    show();
    expect(screen.queryByTestId('survey-upload-link-make')).toBeNull();
  });

  it('opens the public link in a new tab', () => {
    upload = made({ publicLink: { url: '/p/survey/abc', createdAt: '2026-10-25T10:16:00Z', audience: 'public', protectedAcknowledged: false } });
    show();
    const open = screen.getByTestId('survey-upload-link-open');
    expect(open.getAttribute('href')).toBe(`${window.location.origin}/p/survey/abc`);
    expect(open.getAttribute('target')).toBe('_blank');
  });

  it('says why there is no link, and makes one for a protected cave only with the acknowledgement', async () => {
    upload = made({ publicLinkRefusal: 'protected-cave' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('position is protected');
    fireEvent.click(screen.getByTestId('survey-upload-link-make'));
    expect(await screen.findByTestId('survey-upload-link-protected-warning')).toBeInTheDocument();
    const ok = screen.getByTestId('survey-upload-link-dialog-ok');
    expect(ok).toBeDisabled();
    fireEvent.click(ok);
    expect(mintMutate).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId('survey-upload-link-acknowledge'));
    expect(ok).toBeEnabled();
    fireEvent.click(ok);
    await waitFor(() =>
      expect(mintMutate).toHaveBeenCalledWith({ id: 'u1', audience: 'organization', allowProtected: true }),
    );

    cleanup();
    upload = made({ publicLinkRefusal: 'account-off' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('switched off');
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();
  });

  it('says a withdrawn link was withdrawn and offers a new one, but none on an installation that makes no links', () => {
    upload = made({ publicLinkRefusal: 'revoked' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('withdrawn');
    expect(screen.getByTestId('survey-upload-link-make')).toBeInTheDocument();

    cleanup();
    upload = made({ publicLinkRefusal: 'installation-off' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('public links switched off');
    expect(screen.queryByTestId('survey-upload-link-make')).toBeNull();
  });

  it('reads a refusal code it has no wording for as no reason known', () => {
    upload = made({ publicLinkRefusal: 'some-later-reason' });
    show();
    expect(screen.getByTestId('survey-upload-no-link')).toHaveTextContent('can each be the reason');
  });

  it('turns the server\'s refusal to make a link into its sentence', async () => {
    mintMutate.mockRejectedValueOnce(new ApiError(403, 'survey_upload.public_link_forbidden'));
    show();
    fireEvent.click(screen.getByTestId('survey-upload-link-make'));
    fireEvent.click(await screen.findByTestId('survey-upload-link-dialog-ok'));
    expect(await screen.findByText(/takes the right to share the cave/)).toBeInTheDocument();
  });

  it('asks for the acknowledgement when the server says the cave has become protected', async () => {
    mintMutate.mockRejectedValueOnce(new ApiError(409, 'survey_upload.public_link_refused_protected'));
    show();
    fireEvent.click(screen.getByTestId('survey-upload-link-make'));
    fireEvent.click(await screen.findByTestId('survey-upload-link-dialog-ok'));
    expect(await screen.findByTestId('survey-upload-link-acknowledge')).not.toBeChecked();
    expect(screen.getByTestId('survey-upload-link-dialog-ok')).toBeDisabled();
  });

  it('lists the survey\'s versions and points to a newer one', () => {
    groups = [
      {
        id: 's1',
        versions: [
          { id: 'u2', version: '2026-10-26T10:15:00Z', status: 'succeeded', statusPageUrl: '/caves/c1/surveys/s1/uploads/u2' },
          { id: 'u1', version: '2026-10-25T10:15:00Z', status: 'succeeded', statusPageUrl: '/caves/c1/surveys/s1/uploads/u1' },
        ],
      },
    ];
    show();
    expect(screen.getByTestId('survey-upload-versions')).toBeInTheDocument();
    expect(screen.getByTestId('survey-upload-newer')).toBeInTheDocument();
    expect(screen.getAllByRole('link').some((a) => a.getAttribute('href') === '/caves/c1/surveys/s1/uploads/u2')).toBe(true);
  });

  it('says so when the upload is not found', () => {
    upload = undefined;
    uploadError = new ApiError(404, 'survey_upload.not_found');
    show();
    expect(screen.getByTestId('survey-upload-unavailable')).toHaveTextContent('not found');
  });
});
