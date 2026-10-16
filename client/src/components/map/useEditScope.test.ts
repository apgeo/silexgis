// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import { renderHook } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ObjectRights } from '../../api/hooks/objectRights.ts';
import type { MapEditController } from '../../map/mapEdit.ts';
import { useEditScope, type EditScopeInput } from './useEditScope.ts';

function controller() {
  return { setReshapable: vi.fn(), setMode: vi.fn() };
}

const rights = (over: Partial<ObjectRights>): ObjectRights => ({
  canEdit: false,
  canDelete: false,
  canShare: false,
  canManagePermissions: false,
  settled: true,
  ...over,
});

function scope(fake: ReturnType<typeof controller>, over: Partial<EditScopeInput>) {
  const input: EditScopeInput = {
    controller: fake as unknown as MapEditController,
    selectedFeatureId: undefined,
    selectedRights: rights({}),
    mayWriteFeatures: false,
    mayCreateFeatures: false,
    unsavedEdits: 0,
    ...over,
  };
  return renderHook((props: EditScopeInput) => useEditScope(props), { initialProps: input });
}

const withId = (id: string) => {
  const feature = new Feature();
  feature.set('id', id);
  return feature;
};

/** The filter last handed to the controller; undefined is "every feature". */
function lastFilter(fake: ReturnType<typeof controller>) {
  return fake.setReshapable.mock.calls.at(-1)?.[0] as ((feature: Feature) => boolean) | undefined;
}

describe('useEditScope', () => {
  it('limits the reshaping tools to the selected feature when its own rights allow it', () => {
    const fake = controller();
    scope(fake, { selectedFeatureId: 'given', selectedRights: rights({ canEdit: true }) });

    const filter = lastFilter(fake)!;
    expect(filter(withId('given'))).toBe(true);
    expect(filter(withId('another'))).toBe(false);
  });

  it('lets them take hold of nothing saved until the selected feature has answered', () => {
    const fake = controller();
    // The installation-wide stand-in says yes; the feature has not said anything yet.
    const { result } = scope(fake, {
      selectedFeatureId: 'given',
      selectedRights: rights({ canEdit: true, settled: false }),
      mayWriteFeatures: true,
    });

    expect(result.current.mayReshapeSaved).toBe(false);
    expect(lastFilter(fake)!(withId('given'))).toBe(false);
  });

  it('lets a member who may write every feature reshape any, with nothing selected', () => {
    const fake = controller();
    scope(fake, { mayWriteFeatures: true });
    expect(lastFilter(fake)).toBeUndefined();
  });

  it('puts an armed tool down when the toolbar goes, and keeps the toolbar while edits are unsaved', () => {
    const fake = controller();
    const given: EditScopeInput = {
      controller: fake as unknown as MapEditController,
      selectedFeatureId: 'given',
      selectedRights: rights({ canEdit: true }),
      mayWriteFeatures: false,
      mayCreateFeatures: false,
      unsavedEdits: 1,
    };
    const view = scope(fake, given);

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
