// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SpeleolocImportPreview } from '../../api/hooks.ts';

/** Everything below is invented: a made-up recording, party and identifiers. */
const RECORDING = '33333333-3333-4333-8333-333333333333';

const commitMutate = vi.fn();
const navigate = vi.fn();
let asked: { id: string | undefined; body: { options?: { tripUuid?: string | null } } } | null = null;
let preview: { data?: SpeleolocImportPreview; error: unknown; isError: boolean };

const PREVIEW: SpeleolocImportPreview = {
  points: [],
  page: 1,
  pageSize: 25,
  totalItems: 4,
  selectablePointIds: [],
  deviceUsers: [],
  unmappedDeviceUsers: [],
  knownLinks: {},
  caversNotOnRoster: [],
  modelUsable: false,
  proposedCount: 0,
  unresolvedCount: 0,
  recordingTitle: 'Sump trip',
  recordingStartedAt: '2026-09-12T08:00:00Z',
  recordingEndedAt: '2026-09-12T16:00:00Z',
  documentCount: 1,
};

vi.mock('../../api/hooks.ts', () => ({
  useRecordingImportPreview: (
    id: string | undefined,
    body: { options?: { tripUuid?: string | null } },
  ) => {
    asked = { id, body };
    return { ...preview, isFetching: false };
  },
  useCommitRecordingImport: () => ({ mutateAsync: commitMutate, isPending: false }),
  useCavers: () => ({ data: [], isFetching: false }),
  useCavingGroups: () => ({ data: [], isLoading: false }),
  useSurveyModel: () => ({ data: undefined }),
  useSurveyModels: () => ({ data: [], isFetching: false }),
  useFilterVocabulary: () => ({ data: { worlds: [] } }),
  useFilterQuery: () => ({ data: undefined, isFetching: false }),
  useFilterResolve: () => ({ data: [] }),
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useParams: () => ({ recordingId: RECORDING }), useNavigate: () => navigate };
});

const { default: SpeleolocRecordingImportPage } = await import(
  './SpeleolocRecordingImportPage.tsx'
);

function show() {
  return render(
    <MemoryRouter>
      <App>
        <SpeleolocRecordingImportPage />
      </App>
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  asked = null;
  commitMutate.mockReset();
});

describe('SpeleolocRecordingImportPage', () => {
  it('reads the recording named in the address, names it instead of offering a choice, and cannot confirm yet', () => {
    preview = { data: PREVIEW, error: null, isError: false };
    show();

    expect(asked?.id).toBe(RECORDING);
    expect(asked?.body.options?.tripUuid).toBe(RECORDING);
    expect(screen.getByTestId('speleoloc-import-recording-locked').textContent).toContain(
      'Sump trip',
    );
    // There is no recording to choose from an archive.
    expect(screen.queryByTestId('speleoloc-import-recording')).toBeNull();
    // Nothing is chosen yet — no trip, no survey, no scan taken — so the button is dead.
    expect(
      (screen.getByTestId('speleoloc-import-commit') as HTMLButtonElement).disabled,
    ).toBe(true);
    expect(commitMutate).not.toHaveBeenCalled();
  });

  it('says so, and draws no review, when the recording already has its report', () => {
    preview = {
      data: undefined,
      isError: true,
      error: new ApiError(409, 'import.recording_already_converted', 'already', undefined),
    };
    show();

    expect(screen.getByTestId('speleoloc-import-preview-error').textContent).toContain(
      'already been made into a trip report',
    );
    expect(screen.queryByTestId('speleoloc-import-options')).toBeNull();
    expect(
      (screen.getByTestId('speleoloc-import-commit') as HTMLButtonElement).disabled,
    ).toBe(true);
  });
});
