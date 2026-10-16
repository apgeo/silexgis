// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ObjectRights } from '../../api/hooks/objectRights.ts';
import type { MapEditController } from '../../map/mapEdit.ts';
import { reshapeScopeFor, useEditScope, type EditScopeInput } from './useEditScope.ts';

function controller() {
  return { setReshapeScope: vi.fn(), setMode: vi.fn() };
}

const rights = (over: Partial<ObjectRights>): ObjectRights => ({
  canEdit: false,
  canDelete: false,
  canShare: false,
  canManagePermissions: false,
  settled: true,
  ...over,
});

function input(fake: ReturnType<typeof controller>, over: Partial<EditScopeInput>): EditScopeInput {
  return {
    controller: fake as unknown as MapEditController,
    selectedFeatureId: undefined,
    selectedRights: rights({}),
    mayWriteFeatures: false,
    mayCreateFeatures: false,
    unsavedEdits: 0,
    ...over,
  };
}

describe('reshapeScopeFor', () => {
  it('limits a member whose right is on the selected feature alone to that feature', () => {
    expect(reshapeScopeFor('given', rights({ canEdit: true }), false)).toEqual({ kind: 'only', id: 'given' });
    expect(reshapeScopeFor('other', rights({}), false)).toEqual({ kind: 'none' });
    expect(reshapeScopeFor(undefined, rights({}), false)).toEqual({ kind: 'none' });
  });

  it('lets a member who may write every feature reshape any, but one whose own rights withhold it', () => {
    expect(reshapeScopeFor(undefined, rights({}), true)).toEqual({ kind: 'every' });
    expect(reshapeScopeFor('given', rights({ canEdit: true }), true)).toEqual({ kind: 'every' });
    expect(reshapeScopeFor('held-back', rights({}), true)).toEqual({ kind: 'every', except: 'held-back' });
  });

  it('takes nothing from a selected feature that has not answered yet', () => {
    expect(reshapeScopeFor('given', rights({ canEdit: true, settled: false }), false)).toEqual({ kind: 'none' });
  });
});

describe('useEditScope', () => {
  it('puts the scope on the controller, and offers the tools for a selected feature only once it has answered', () => {
    const fake = controller();
    const view = renderHook((props: EditScopeInput) => useEditScope(props), {
      initialProps: input(fake, { selectedFeatureId: 'given', selectedRights: rights({ canEdit: true }) }),
    });
    expect(fake.setReshapeScope).toHaveBeenLastCalledWith({ kind: 'only', id: 'given' });
    expect(view.result.current.mayReshapeSaved).toBe(true);

    // Another feature selected: until it answers, the tools are not offered and the scope is left
    // as it was — changing it would drop the vertex the selecting tap took hold of.
    fake.setReshapeScope.mockClear();
    view.rerender(input(fake, { selectedFeatureId: 'other', selectedRights: rights({ canEdit: true, settled: false }) }));
    expect(view.result.current).toMatchObject({ mayReshapeSaved: false, rightsPending: true });
    expect(fake.setReshapeScope).not.toHaveBeenCalled();

    view.rerender(input(fake, { selectedFeatureId: 'other', selectedRights: rights({ canEdit: true }) }));
    expect(fake.setReshapeScope).toHaveBeenLastCalledWith({ kind: 'only', id: 'other' });
  });

  it('puts an armed tool down when the toolbar goes, and keeps the toolbar while edits are unsaved', () => {
    const fake = controller();
    const given = input(fake, { selectedFeatureId: 'given', selectedRights: rights({ canEdit: true }), unsavedEdits: 1 });
    const view = renderHook((props: EditScopeInput) => useEditScope(props), { initialProps: given });

    // Another feature, not this member's to change, selected while the first one's edit is unsaved.
    const other = { ...given, selectedFeatureId: 'other', selectedRights: rights({}) };
    view.rerender(other);
    expect(view.result.current.toolbarShown).toBe(true);
    expect(fake.setMode).not.toHaveBeenCalled();

    // Saved (or discarded): nothing left the toolbar is for.
    view.rerender({ ...other, unsavedEdits: 0 });
    expect(view.result.current.toolbarShown).toBe(false);
    expect(fake.setMode).toHaveBeenCalledWith('none');
  });
});
