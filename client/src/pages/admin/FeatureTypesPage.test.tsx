// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { FeatureType, FeatureTypeWrite } from '../../api/hooks.ts';

const featureTypes: FeatureType[] = [
  {
    id: 1,
    code: 'cave_place',
    name: 'Cave place',
    description: null,
    sortOrder: 10,
    category: 'underground',
    acceptedGeometryClasses: ['point', 'multiPoint'],
    requiresParent: true,
    propertiesSchemaVersion: 1,
    protectedDisplay: 'withhold',
    symbolFile: 'generic_feature.png',
    style: null,
    propertiesSchema: null,
    isSeeded: true,
    boundKind: 'generic',
  },
  {
    id: 2,
    code: 'bat_roost',
    name: 'Bat roost',
    description: null,
    sortOrder: 900,
    category: 'surface',
    acceptedGeometryClasses: ['point'],
    requiresParent: false,
    propertiesSchemaVersion: 3,
    protectedDisplay: 'snapPoint',
    symbolFile: null,
    style: null,
    propertiesSchema: null,
    isSeeded: false,
    boundKind: 'generic',
  },
  {
    id: 3,
    code: 'cave',
    name: 'Cave',
    description: null,
    sortOrder: 320,
    category: 'underground',
    acceptedGeometryClasses: ['point', 'multiPoint'],
    requiresParent: false,
    propertiesSchemaVersion: 1,
    protectedDisplay: 'snapPoint',
    symbolFile: null,
    style: null,
    propertiesSchema: '{"type":"object","properties":{"speologieId":{"type":"string","readOnly":true}}}',
    isSeeded: true,
    boundKind: 'cave',
  },
];

const updateMutate = vi.fn((write: FeatureTypeWrite & { id: number }) => Promise.resolve(write));
const createMutate = vi.fn((write: FeatureTypeWrite) => Promise.resolve(write));
const deleteMutate = vi.fn((id: number) => Promise.resolve(id));

const capabilities = { domains: { taxonomies: 'read, write, delete' } };

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    hasAccessAction: actual.hasAccessAction,
    useCapabilities: () => ({ data: capabilities }),
    useFeatureTypes: () => ({ data: featureTypes, isLoading: false }),
    useCreateFeatureType: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateFeatureType: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteFeatureType: () => ({ mutateAsync: deleteMutate, isPending: false }),
  };
});

const { default: FeatureTypesPage } = await import('./FeatureTypesPage.tsx');

function renderPage() {
  return render(
    <App>
      <FeatureTypesPage />
    </App>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('FeatureTypesPage', () => {
  it('lists each kind with its shapes, its protected display and whether it ships', () => {
    renderPage();

    expect(screen.getByText('Cave place')).toBeInTheDocument();
    expect(screen.getByText('Not shown at all')).toBeInTheDocument();
    expect(screen.getAllByText('Several points')).toHaveLength(2);
    expect(screen.getAllByText('Ships with the product')).toHaveLength(2);
    expect(screen.getByText('v3')).toBeInTheDocument();
    // A shipped kind cannot be deleted, so it is not offered; the club's own can.
    expect(screen.getAllByText('Delete')).toHaveLength(1);
  });

  it('does not offer what a shipped kind keeps, and offers it for a kind the club added', async () => {
    renderPage();

    fireEvent.click(screen.getAllByText('Edit')[0]);
    expect(await screen.findByLabelText('Code')).toBeDisabled();
    expect(screen.getByRole('switch')).toBeDisabled();
    expect(
      screen.getByText(/This kind ships with the product/),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText('Cancel'));

    fireEvent.click(screen.getAllByText('Edit')[1]);
    await waitFor(() => expect(screen.getByLabelText('Code')).toHaveValue('bat_roost'));
    expect(screen.getByLabelText('Code')).toBeEnabled();
    expect(screen.getByRole('switch')).toBeEnabled();
  });

  it('sends emptied boxes as "none" and keeps the shapes it was given', async () => {
    renderPage();

    fireEvent.click(screen.getAllByText('Edit')[1]);
    const style = await screen.findByLabelText('Map style overrides (JSON object)');
    fireEvent.change(style, { target: { value: '   ' } });
    fireEvent.click(screen.getByText('Save'));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled());
    const sent = updateMutate.mock.calls[0][0];
    expect(sent.id).toBe(2);
    expect(sent.style).toBeNull();
    expect(sent.propertiesSchema).toBeNull();
    expect(sent.acceptedGeometryClasses).toEqual(['point']);
  });

  it('marks the type every cave carries and names the refusal of a schema that would drop its fields', async () => {
    updateMutate.mockRejectedValueOnce(
      new ApiError(400, 'feature_type.bound_schema_locked', 'This type keeps its fields.'),
    );
    renderPage();

    expect(screen.getByText('Every cave')).toBeInTheDocument();
    fireEvent.click(screen.getAllByText('Edit')[2]);
    await waitFor(() => expect(screen.getByLabelText('Code')).toHaveValue('cave'));
    expect(screen.getByTestId('feature-type-bound-note')).toBeInTheDocument();
    expect(screen.getByLabelText('Code')).toBeDisabled();
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText(/keeps the fields it ships with/)).toBeInTheDocument();
  });

  it('names the refusal when features already use the kind', async () => {
    updateMutate.mockRejectedValueOnce(
      new ApiError(409, 'feature_type.in_use', 'Features of this type exist.'),
    );
    renderPage();

    fireEvent.click(screen.getAllByText('Edit')[1]);
    await screen.findByLabelText('Code');
    fireEvent.click(screen.getByText('Save'));

    expect(await screen.findByText(/Features of this kind exist/)).toBeInTheDocument();
  });
});
