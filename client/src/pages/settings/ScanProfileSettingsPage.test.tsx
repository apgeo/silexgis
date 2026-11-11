// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  ProcessingProfile,
  ProcessingProfileImportResult,
  ScanStepParameterSchema,
} from '../../api/hooks.ts';

/**
 * The processing-profile pages: the list per scope, the editor generated from a step's schema,
 * and the import with its report.
 *
 * What is under test is what the screens owe the person: a built-in is never offered a save in
 * place, only a copy; what may be deleted is what the server says per profile; a changed setting
 * is marked and counted; what is saved is the values as edited; and an import is kept only after
 * its dry run was accepted. Every profile, key and value below is invented.
 */

const createMutate = vi.fn();
const updateMutate = vi.fn();
const copyMutate = vi.fn();
const deleteMutate = vi.fn();
const importMutate = vi.fn();
const exportProfile = vi.fn();
let profiles: ProcessingProfile[];
let canShare: boolean;

const schema: ScanStepParameterSchema = {
  stepId: 'hdmapping.odometry',
  toolVersion: '0.90-test',
  source: 'stored',
  schema: {
    type: 'object',
    properties: {
      values: {
        type: 'object',
        properties: {
          filter_points: {
            type: 'object',
            properties: {
              decimation: { type: 'number', default: 0.1 },
              use_filter: { type: 'boolean', default: true },
            },
          },
          lidar_odometry_control: {
            type: 'object',
            properties: { rounds: { type: 'integer', default: 30 } },
          },
        },
      },
    },
  },
  defaults: { values: { filter_points: { decimation: 0.1, use_filter: true }, lidar_odometry_control: { rounds: 30 } } },
};

const profileOf = (over: Partial<ProcessingProfile>): ProcessingProfile => ({
  id: 'p1',
  stepId: 'hdmapping.odometry',
  toolVersion: '0.90-test',
  name: 'Profil inventat',
  description: null,
  scope: 'personal',
  ownerUserId: 'u1',
  cavingGroupId: null,
  values: { values: { filter_points: { decimation: 0.1, use_filter: true }, lidar_odometry_control: { rounds: 30 } } },
  origin: 'edited',
  builtIn: false,
  mayEdit: true,
  mayDelete: true,
  createdAt: '2026-11-01T10:00:00Z',
  updatedAt: '2026-11-01T10:00:00Z',
  ...over,
});

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  const mutation = (fn: typeof createMutate) => ({ mutateAsync: fn, isPending: false });
  return {
    ...actual,
    useProcessingProfiles: () => ({ data: profiles, isPending: false, isError: false, refetch: vi.fn() }),
    useScanStepParameterSchema: () => ({ data: schema, isPending: false, isError: false, refetch: vi.fn() }),
    useCavingGroups: () => ({ data: [{ id: 'g1', name: 'Clubul de test' }], isPending: false }),
    useCan: () => canShare,
    useCreateProcessingProfile: () => mutation(createMutate),
    useUpdateProcessingProfile: () => mutation(updateMutate),
    useCopyProcessingProfile: () => mutation(copyMutate),
    useDeleteProcessingProfile: () => mutation(deleteMutate),
    useImportProcessingProfile: () => mutation(importMutate),
  };
});

// The page downloads through the shared helper; the test records the address it asked for.
vi.mock('../../api/download.ts', () => ({ downloadFile: (url: string) => exportProfile(url) }));

const { default: ScanProfileSettingsPage } = await import('./ScanProfileSettingsPage.tsx');
const { profilesByScope } = await import('../../components/scanProfiles/profileRules.ts');

function show() {
  return render(
    <App>
      <ScanProfileSettingsPage />
    </App>,
  );
}

beforeEach(() => {
  for (const fn of [createMutate, updateMutate, copyMutate, deleteMutate, importMutate, exportProfile]) fn.mockReset();
  exportProfile.mockResolvedValue(undefined);
  canShare = false;
  profiles = [
    profileOf({
      id: 'b1',
      name: 'HDMapping defaults',
      scope: 'installation',
      ownerUserId: null,
      origin: 'builtIn',
      builtIn: true,
      mayEdit: false,
      mayDelete: false,
    }),
    profileOf({
      id: 'g1p',
      name: 'Al clubului',
      scope: 'cavingGroup',
      cavingGroupId: 'g1',
      ownerUserId: null,
      mayEdit: false,
      mayDelete: false,
    }),
    profileOf({ id: 'p1', name: 'Al meu', origin: 'imported:run.toml' }),
    profileOf({ id: 'other', stepId: 'py3dtiles.tile', name: 'Alt pas' }),
  ];
});

afterEach(cleanup);

describe('the list of processing profiles', () => {
  it('sorts one step by scope, built-ins first, and leaves other steps out', () => {
    const byScope = profilesByScope(
      [profileOf({ id: 'a', name: 'B' }), profileOf({ id: 'b', name: 'A' }), profileOf({ id: 'c', name: 'Z', builtIn: true })],
      'hdmapping.odometry',
    );
    expect(byScope.personal.map((p) => p.id)).toEqual(['c', 'b', 'a']);
    expect(profilesByScope(profiles, 'hdmapping.odometry').personal.map((p) => p.id)).toEqual(['p1']);
  });

  it('lists each scope with its own rows, and offers delete only where the server allows it', () => {
    show();
    const installation = screen.getByTestId('profile-scope-installation');
    expect(within(installation).getByText('HDMapping defaults')).toBeTruthy();
    expect(within(installation).getByText('Built-in')).toBeTruthy();
    expect(within(installation).queryByTestId('profile-delete')).toBeNull();

    const group = screen.getByTestId('profile-scope-cavingGroup');
    expect(within(group).getByText('Clubul de test')).toBeTruthy();
    expect(within(group).queryByTestId('profile-delete')).toBeNull();

    const mine = screen.getByTestId('profile-scope-personal');
    expect(within(mine).getByText('Al meu')).toBeTruthy();
    expect(within(mine).getByText('Imported from run.toml')).toBeTruthy();
    expect(within(mine).getByTestId('profile-delete')).toBeTruthy();
    // A profile of another step is not on this step's list.
    expect(screen.queryByText('Alt pas')).toBeNull();
  });

  it('duplicates a profile as a copy of one’s own', async () => {
    copyMutate.mockResolvedValue(profileOf({ id: 'copy' }));
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-installation')).getByTestId('profile-duplicate'));
    await waitFor(() => expect(copyMutate).toHaveBeenCalledWith({ id: 'b1' }));
  });

  it('deletes a profile only after the question is confirmed', async () => {
    deleteMutate.mockResolvedValue(undefined);
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-personal')).getByTestId('profile-delete'));
    expect(deleteMutate).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(deleteMutate).toHaveBeenCalledWith('p1'));
  });

  it('saves an odometry profile as HDMapping’s own file or as a profile file', async () => {
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-personal')).getByTestId('profile-export'));
    fireEvent.click(await screen.findByRole('menuitem', { name: 'HDMapping parameter file (TOML)' }));
    await waitFor(() => expect(exportProfile).toHaveBeenCalledWith('/api/v1/scan-profiles/p1/export?format=toml'));
  });
});

describe('the profile editor', () => {
  it('opens a built-in for reading and copying, never for saving in place', async () => {
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-installation')).getByTestId('profile-open'));
    expect(await screen.findByTestId('profile-built-in')).toBeTruthy();
    expect(screen.queryByTestId('profile-save')).toBeNull();
    expect(screen.getByTestId('profile-save-as').textContent).toContain('Save as my copy');
  });

  it('keeps a built-in’s edited settings as a copy, leaving the built-in as it was', async () => {
    copyMutate.mockResolvedValue(profileOf({ id: 'copy' }));
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-installation')).getByTestId('profile-open'));
    await screen.findByTestId('profile-form');
    const rounds = screen
      .getAllByTestId('profile-field')
      .find((row) => row.getAttribute('data-key') === 'values.lidar_odometry_control.rounds');
    // The first section is open by itself; the second is opened by its header.
    if (!rounds) fireEvent.click(screen.getByText('lidar_odometry_control'));
    const row = await waitFor(() => {
      const found = screen
        .getAllByTestId('profile-field')
        .find((r) => r.getAttribute('data-key') === 'values.lidar_odometry_control.rounds');
      if (!found) throw new Error('rounds not shown');
      return found;
    });
    fireEvent.change(within(row).getByRole('spinbutton'), { target: { value: '12' } });
    await waitFor(() => expect(row.getAttribute('data-changed')).toBe('true'));

    fireEvent.click(screen.getByTestId('profile-save-as'));
    fireEvent.click(await screen.findByTestId('profile-save-as-confirm'));
    await waitFor(() => expect(copyMutate).toHaveBeenCalledTimes(1));
    const sent = copyMutate.mock.calls[0]![0] as {
      id: string;
      name: string;
      scope: string;
      values: { values: { lidar_odometry_control: { rounds: number } } };
    };
    expect(sent).toMatchObject({ id: 'b1', name: 'HDMapping defaults (copy)', scope: 'personal' });
    expect(sent.values.values.lidar_odometry_control.rounds).toBe(12);
    expect(updateMutate).not.toHaveBeenCalled();
  });

  it('marks and counts a changed setting, and saves the values as edited', async () => {
    updateMutate.mockResolvedValue(profileOf({}));
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-personal')).getByTestId('profile-open'));
    await screen.findByTestId('profile-form');
    expect(screen.getByTestId('profile-changed-count').textContent).toContain('0 settings differ');

    const decimation = screen
      .getAllByTestId('profile-field')
      .find((row) => row.getAttribute('data-key') === 'values.filter_points.decimation')!;
    expect(decimation.getAttribute('data-changed')).toBe('false');
    fireEvent.change(within(decimation).getByRole('spinbutton'), { target: { value: '0.25' } });
    await waitFor(() => expect(decimation.getAttribute('data-changed')).toBe('true'));
    expect(screen.getByTestId('profile-changed-count').textContent).toContain('1 setting differs');

    // Narrowed to what differs, only the changed setting is left.
    fireEvent.click(screen.getByTestId('profile-only-changed'));
    await waitFor(() => expect(screen.getAllByTestId('profile-field')).toHaveLength(1));

    fireEvent.click(screen.getByTestId('profile-save'));
    await waitFor(() => expect(updateMutate).toHaveBeenCalledTimes(1));
    const sent = updateMutate.mock.calls[0]![0] as { id: string; values: { values: { filter_points: { decimation: number; use_filter: boolean } } } };
    expect(sent.id).toBe('p1');
    expect(sent.values.values.filter_points).toEqual({ decimation: 0.25, use_filter: true });
  });

  it('names the keys the server refused when a save is refused', async () => {
    updateMutate.mockRejectedValue(
      new ApiError(400, 'processing_profile.values_invalid', 'values.filter_points.decimation: must be a number'),
    );
    show();
    fireEvent.click(within(screen.getByTestId('profile-scope-personal')).getByTestId('profile-open'));
    await screen.findByTestId('profile-form');
    fireEvent.click(screen.getByTestId('profile-save'));
    const error = await screen.findByTestId('profile-save-error');
    expect(error.textContent).toContain('values.filter_points.decimation: must be a number');
  });

  it('offers only one’s own scope to somebody without the profiles right, and every scope to somebody with it', async () => {
    show();
    fireEvent.click(screen.getByTestId('profile-new'));
    let scopes = await screen.findByTestId('profile-scope');
    expect(within(scopes).getAllByRole('radio')).toHaveLength(1);
    cleanup();

    canShare = true;
    show();
    fireEvent.click(screen.getByTestId('profile-new'));
    scopes = await screen.findByTestId('profile-scope');
    expect(within(scopes).getAllByRole('radio')).toHaveLength(3);
  });
});

describe('importing a parameter file', () => {
  const report = (over: Partial<ProcessingProfileImportResult['report']>): ProcessingProfileImportResult => ({
    profile: null,
    values: {},
    report: {
      stepId: 'hdmapping.odometry',
      format: 'toml',
      taken: ['filter_points.decimation'],
      unknown: ['filter_points.newer_key'],
      defaulted: ['lidar_odometry_control.rounds'],
      fixed: [],
      droppedSections: ['paths'],
      refused: [],
      fileVersion: '0.89-test',
      installedVersion: '0.90-test',
      versionDiffers: true,
      accepted: true,
      ...over,
    },
  });

  async function pick(content: string) {
    show();
    fireEvent.click(screen.getByTestId('profile-import'));
    await screen.findByText('Import a parameter file');
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File([content], 'run.toml', { type: 'application/toml' });
    fireEvent.change(input, { target: { files: [file] } });
  }

  it('shows the dry run’s report and keeps the file only when it was accepted', async () => {
    importMutate.mockResolvedValueOnce(report({})).mockResolvedValueOnce({ ...report({}), profile: profileOf({}) });
    await pick('[filter_points]\ndecimation = 0.2\n');

    const shown = await screen.findByTestId('profile-import-report');
    expect(importMutate.mock.calls[0]![0]).toMatchObject({ fileName: 'run.toml', dryRun: true });
    expect(within(shown).getByTestId('profile-import-unknown').textContent).toContain('filter_points.newer_key');
    expect(within(shown).getByTestId('profile-import-version').textContent).toContain('0.89-test');

    fireEvent.click(screen.getByTestId('profile-import-keep'));
    await waitFor(() => expect(importMutate).toHaveBeenCalledTimes(2));
    expect(importMutate.mock.calls[1]![0]).toMatchObject({ dryRun: false, scope: 'personal', cavingGroupId: null });
  });

  it('names each refused key and does not offer to keep the file', async () => {
    importMutate.mockResolvedValueOnce(
      report({ accepted: false, refused: [{ key: 'filter_points.decimation', reason: 'must be a number' }] }),
    );
    await pick('[filter_points]\ndecimation = "x"\n');

    const refused = await screen.findByTestId('profile-import-refused');
    expect(refused.textContent).toContain('filter_points.decimation');
    expect((screen.getByTestId('profile-import-keep') as HTMLButtonElement).disabled).toBe(true);
  });
});
