// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SurveySourceInfo } from '../../api/hooks.ts';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { SURVEY_SOURCE_PROBLEM_MESSAGE_KEYS } from './surveySourceProblems.ts';

const uploadMutate = vi.fn();
const deleteMutate = vi.fn();

function source(overrides: Partial<SurveySourceInfo> = {}): SurveySourceInfo {
  return {
    id: 's1',
    caveId: 'c1',
    kind: 'therionSource',
    name: 'grind.th',
    fileName: 'grind.th',
    description: null,
    documentId: 'd1',
    fileId: 'f1',
    mediaType: 'application/x-therion',
    sizeBytes: 4096,
    versionNumber: 1,
    contentUrl: '/api/v1/files/f1/content?token=t',
    createdAt: '2026-08-29T06:00:00Z',
    updatedAt: '2026-08-29T06:00:00Z',
    ...overrides,
  } as SurveySourceInfo;
}

let sources: SurveySourceInfo[] = [];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSurveySources: () => ({ data: sources }),
    useUploadSurveySource: () => ({ mutateAsync: uploadMutate, isPending: false }),
    useDeleteSurveySource: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: SurveySourceSection, ACCEPTED_SOURCE_EXTENSIONS } = await import(
  './SurveySourceSection.tsx'
);

function show(canEdit = true) {
  return render(
    <App>
      <SurveySourceSection caveId="c1" canEdit={canEdit} />
    </App>,
  );
}

/** Choose a file the way a person does: through the input the uploader renders. */
function chooseFile(name: string) {
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [new File(['survey data'], name)] } });
}

beforeEach(() => {
  uploadMutate.mockReset().mockResolvedValue(source());
  deleteMutate.mockReset().mockResolvedValue(undefined);
  sources = [];
});

afterEach(cleanup);

describe('SurveySourceSection', () => {
  it('offers the archive only to somebody who may change the cave', () => {
    show(false);
    expect(screen.queryByRole('button', { name: /Archive a source/ })).toBeNull();
    cleanup();
    show(true);
    expect(screen.getByRole('button', { name: /Archive a source/ })).toBeInTheDocument();
  });

  it('names every accepted kind to the file picker', () => {
    show();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    // The list the server accepts. It is a hint, not a gate — the server refuses the rest — but a
    // kind missing here cannot be chosen at all, which reads as the archive not accepting it.
    for (const extension of ['.th', '.th2', '.thconfig', '.svx', '.log', '.zip']) {
      expect(ACCEPTED_SOURCE_EXTENSIONS.split(',')).toContain(extension);
    }
    expect(input.getAttribute('accept')).toBe(ACCEPTED_SOURCE_EXTENSIONS);
  });

  it('lists an archived source with its kind and revision', () => {
    sources = [source({ kind: 'therionLog', name: 'therion.log', versionNumber: 3 })];
    show();
    expect(screen.getByText('therion.log')).toBeInTheDocument();
    expect(screen.getByText('Compilation log')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('sends a chosen file to the archive', async () => {
    show();
    chooseFile('grind.th');
    await waitFor(() => expect(uploadMutate).toHaveBeenCalledTimes(1));
    expect(uploadMutate.mock.calls[0][0].caveId).toBe('c1');
    expect(uploadMutate.mock.calls[0][0].file.name).toBe('grind.th');
  });

  it('says what a refusal was about rather than that something failed', async () => {
    // The refusal this archive exists to make: the bytes are not the format the name claims.
    uploadMutate.mockRejectedValue(new ApiError(400, 'survey_source.content_mismatch'));
    show();
    chooseFile('trojan.svx');
    expect(await screen.findByText(/not what its name says/)).toBeInTheDocument();
  });

  it('has both locales for every refusal it can show', () => {
    // Looked up through a variable at the call site, so the untranslated-key scan cannot see any
    // of these; a missing Romanian key would surface as an English sentence in a Romanian page.
    for (const key of Object.values(SURVEY_SOURCE_PROBLEM_MESSAGE_KEYS)) {
      const path = key.split('.');
      for (const locale of [en, ro] as unknown as Record<string, unknown>[]) {
        const value = path.reduce<unknown>(
          (node, part) => (node as Record<string, unknown> | undefined)?.[part],
          locale,
        );
        expect(typeof value, `${key} in one of the locales`).toBe('string');
      }
    }
  });
});
