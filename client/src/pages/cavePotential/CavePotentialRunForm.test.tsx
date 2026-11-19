// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CavePotentialModelInfo } from '../../api/hooks/cavePotential.ts';
import { decodeFilter } from '../../filters/url.ts';

const state = vi.hoisted(() => ({
  featureCalls: [] as { params: Record<string, unknown>; enabled: boolean }[],
  items: [] as unknown[],
  runSpy: vi.fn(),
}));

vi.mock('../../api/hooks/features.ts', () => ({
  useFeatures: (params: Record<string, unknown>, enabled = true) => {
    state.featureCalls.push({ params, enabled });
    return { data: enabled ? { items: state.items } : undefined, isFetching: false };
  },
}));

vi.mock('../../api/hooks/cavePotential.ts', () => ({
  useRunCavePotentialModel: () => ({ mutateAsync: state.runSpy, isPending: false }),
}));

const { default: CavePotentialRunForm } = await import('./CavePotentialRunForm.tsx');

const model = {
  id: 'm-1',
  name: 'Near the sinkholes',
  usesPositions: false,
} as CavePotentialModelInfo;

function area(id: string, name: string, type: 'Polygon' | 'Point' = 'Polygon') {
  return { id, name, geometry: { type, coordinates: [] } };
}

function show() {
  return render(
    <AntApp>
      <CavePotentialRunForm model={model} onClose={() => {}} />
    </AntApp>,
  );
}

async function typeIntoArea(text: string) {
  const input = screen.getByRole('combobox', { name: /area|zon/i });
  fireEvent.mouseDown(input);
  fireEvent.change(input, { target: { value: text } });
}

beforeEach(() => {
  state.featureCalls = [];
  state.items = [];
  state.runSpy.mockReset();
});

afterEach(cleanup);

describe('the run form finds an area by name', () => {
  it('asks the server for nothing until a name is typed', () => {
    show();

    expect(state.featureCalls.length).toBeGreaterThan(0);
    expect(state.featureCalls.every((call) => !call.enabled)).toBe(true);
  });

  it('narrows to areas on the server, so a page of matches is a page of areas', async () => {
    show();
    await typeIntoArea('plateau');

    await waitFor(() => expect(state.featureCalls.some((call) => call.enabled)).toBe(true));
    const asked = state.featureCalls.find((call) => call.enabled)!.params;

    expect(asked.search).toBe('plateau');
    expect(asked.pageSize).toBe(50);
    // The category is a condition of the filter the request carries, not something checked after
    // the page has come back: an area beyond the first fifty matches of every kind is still found.
    expect(typeof asked.filter).toBe('string');
    const document = decodeFilter(asked.filter as string);
    expect(JSON.stringify(document)).toContain('"field":"category"');
    expect(JSON.stringify(document)).toContain('"value":"area"');
    // And only that: no kind, type or name condition rides in the token.
    expect(JSON.stringify(document)).not.toContain('"field":"name"');
  });

  it('offers the polygons the server returned and not a point of the same name', async () => {
    state.items = [area('a-1', 'Invented plateau'), area('a-2', 'Invented plateau spring', 'Point')];
    show();
    await typeIntoArea('invented');

    expect(await screen.findByText('Invented plateau')).toBeTruthy();
    expect(screen.queryByText('Invented plateau spring')).toBeNull();
  });
});
