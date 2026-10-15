// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { cavingGroupKey, foldName } from './cavingGroupNames.ts';
import { cavingGroupCreateProblem } from './cavingGroupProblems.ts';

const createMutate = vi.fn();
let mayCreate = true;

const groups = [
  { id: 'g-1', name: 'Clubul Speo Ursus', slug: 'clubul-speo-ursus', type: 'cavingClub', description: null, website: null, memberCount: 3, canAnnounce: false },
  { id: 'g-2', name: 'Ştiinţa Cluj', slug: 'stiinta-cluj', type: 'cavingClub', description: null, website: null, memberCount: 5, canAnnounce: false },
];

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: groups, isLoading: false }),
  useCreateCavingGroup: () => ({ mutateAsync: createMutate, isPending: false }),
  useCan: (domain: string, action: string) => domain === 'cavingGroups' && action === 'create' && mayCreate,
}));

const { default: CavingGroupPicker } = await import('./CavingGroupPicker.tsx');

const onChange = vi.fn();

beforeEach(async () => {
  await i18n.changeLanguage('en');
  mayCreate = true;
  createMutate.mockReset();
  onChange.mockReset();
});
afterEach(cleanup);

function show() {
  render(
    <App>
      <CavingGroupPicker onChange={onChange} />
    </App>,
  );
  return screen.getByRole('combobox');
}

function type(input: HTMLElement, text: string) {
  fireEvent.mouseDown(input);
  fireEvent.change(input, { target: { value: text } });
}

describe('cavingGroupKey', () => {
  it('writes the key the way the server writes a group slug', () => {
    expect(cavingGroupKey('  Ştiinţa   Cluj ')).toBe('stiinta-cluj');
    expect(cavingGroupKey('Clubul  Speo-Ursus!')).toBe('clubul-speo-ursus');
    expect(cavingGroupKey('--A.B--')).toBe('a-b');
    expect(cavingGroupKey('***')).toBe('');
  });

  it('folds case and diacritics for searching', () => {
    expect(foldName('ŞTIINŢA')).toBe('stiinta');
  });
});

describe('CavingGroupPicker', () => {
  it('finds a group by a name typed without its diacritics, and offers no copy of it', async () => {
    const input = show();
    type(input, 'stiinta cluj');

    expect(await screen.findByText('Ştiinţa Cluj')).toBeInTheDocument();
    expect(screen.queryByText(/Create «/)).toBeNull();
  });

  it('offers no copy of a name that differs only in case and punctuation', async () => {
    const input = show();
    type(input, 'CLUBUL speo-ursus');

    expect(await screen.findByText('Clubul Speo Ursus')).toBeInTheDocument();
    expect(screen.queryByText(/Create «/)).toBeNull();
  });

  it('creates a group nobody has listed yet and selects it', async () => {
    createMutate.mockResolvedValue({ ...groups[0], id: 'g-new', name: 'Avenul Negru', slug: 'avenul-negru' });
    const input = show();
    type(input, '  Avenul Negru ');

    fireEvent.click(await screen.findByText('Create «Avenul Negru»'));

    await waitFor(() =>
      expect(createMutate).toHaveBeenCalledWith({ name: 'Avenul Negru', type: 'cavingClub', description: null, website: null }),
    );
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('g-new'));
    expect(await screen.findByText('Avenul Negru was added to the caving groups.')).toBeInTheDocument();
  });

  it('says so when the name was taken in the meantime, and selects nothing', async () => {
    createMutate.mockRejectedValue(new ApiError(400, 'caving_group.name_taken'));
    const input = show();
    type(input, 'Avenul Negru');

    fireEvent.click(await screen.findByText('Create «Avenul Negru»'));

    expect(await screen.findByText(/A caving group with this name already exists/)).toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });

  it('makes no offer to somebody who may not create caving groups', async () => {
    mayCreate = false;
    const input = show();
    type(input, 'Avenul Negru');

    expect(await screen.findByText('No caving group by that name.')).toBeInTheDocument();
    expect(screen.queryByText(/Create «/)).toBeNull();
  });

  it('chooses an existing group without creating anything', async () => {
    const input = show();
    type(input, 'ursus');

    fireEvent.click(await screen.findByText('Clubul Speo Ursus'));

    expect(onChange).toHaveBeenCalledWith('g-1');
    expect(createMutate).not.toHaveBeenCalled();
  });

  it('has words for every refusal it names', () => {
    const t = i18n.getFixedT('ro');
    const generic = t('cavingGroupPicker.createFailed');
    for (const code of ['caving_group.name_taken', 'caving_group.name_invalid', 'access.create_forbidden', 'validation.failed']) {
      expect(cavingGroupCreateProblem(new ApiError(400, code), t), code).not.toBe(generic);
    }
  });
});
