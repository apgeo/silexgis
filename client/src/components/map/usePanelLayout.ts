// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useMemo, useRef, useState } from 'react';
import { useUiDefaults } from '../../api/hooks.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { mergePanelPrefs, type PanelPrefs, type PanelScope } from '../../stores/panelPrefs.ts';
import {
  DEFAULT_COLLAPSED,
  DEFAULT_HIDDEN,
  resolveSectionOrder,
  type PanelSectionId,
} from './panelSections.ts';

/**
 * One panel's arrangement, and the four things a person can do to it: open a section, hide a
 * section, drag one somewhere else, move one with the keyboard.
 *
 * Everything is read through and written back to the stored preferences for this scope, so the
 * arrangement survives a reload without any component holding it. Nothing here fetches; the point
 * of collapsing is that the section's own queries never run.
 */
export function usePanelLayout(scope: PanelScope) {
  const personal = useUiPrefsStore((s) => s.panels[scope]);
  const setPanelPrefs = useUiPrefsStore((s) => s.setPanelPrefs);
  // The installation's starting arrangement fills in only what this person has not chosen. It is
  // a default and never a policy: publishing a section order must not undo somebody's width.
  const installation = useUiDefaults().data?.panel as
    | Partial<Record<PanelScope, PanelPrefs>>
    | undefined;
  const prefs = useMemo(
    () => mergePanelPrefs(installation?.[scope], personal),
    [installation, scope, personal],
  );

  const order = useMemo(() => resolveSectionOrder(prefs?.order), [prefs?.order]);
  const hidden = useMemo(
    () => new Set<PanelSectionId>(prefs?.hidden ?? DEFAULT_HIDDEN),
    [prefs?.hidden],
  );
  const collapsed = useMemo(
    () => new Set<PanelSectionId>(prefs?.collapsed ?? DEFAULT_COLLAPSED),
    [prefs?.collapsed],
  );

  const visible = useMemo(() => order.filter((id) => !hidden.has(id)), [order, hidden]);

  const toggle = useCallback(
    (id: PanelSectionId) => {
      const next = new Set(collapsed);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      setPanelPrefs(scope, { collapsed: [...next] });
    },
    [collapsed, scope, setPanelPrefs],
  );

  const setHidden = useCallback(
    (id: PanelSectionId, value: boolean) => {
      const next = new Set(hidden);
      if (value) {
        next.add(id);
      } else {
        next.delete(id);
      }
      setPanelPrefs(scope, { hidden: [...next] });
    },
    [hidden, scope, setPanelPrefs],
  );

  /** Puts one section where another currently is, keeping every other section's relative order. */
  const move = useCallback(
    (id: PanelSectionId, target: PanelSectionId) => {
      if (id === target) {
        return;
      }
      const next = order.filter((x) => x !== id);
      next.splice(next.indexOf(target), 0, id);
      setPanelPrefs(scope, { order: next });
    },
    [order, scope, setPanelPrefs],
  );

  /**
   * Nudges a section one place up or down *among the sections that are showing*.
   *
   * Stepping through the full order would look broken: pressing "up" once against a hidden
   * neighbour would appear to do nothing at all, and pressing it twice would jump two places.
   */
  const nudge = useCallback(
    (id: PanelSectionId, direction: -1 | 1) => {
      const at = visible.indexOf(id);
      const neighbour = visible[at + direction];
      if (neighbour !== undefined) {
        move(id, neighbour);
      }
    },
    [visible, move],
  );

  const reset = useCallback(
    () => setPanelPrefs(scope, { order: undefined, collapsed: undefined, hidden: undefined }),
    [scope, setPanelPrefs],
  );

  // Which section is under the pointer during a drag. A ref rather than state for the source,
  // because a re-render mid-drag would otherwise lose which section is being carried.
  const draggingRef = useRef<PanelSectionId | null>(null);
  const [dragging, setDragging] = useState<PanelSectionId | null>(null);

  const dragHandlers = useCallback(
    (id: PanelSectionId) => ({
      dragging: dragging === id,
      onDragStart: () => {
        draggingRef.current = id;
        setDragging(id);
      },
      onDragOver: () => {
        const source = draggingRef.current;
        if (source && source !== id) {
          move(source, id);
        }
      },
      onDrop: () => {
        draggingRef.current = null;
        setDragging(null);
      },
    }),
    [dragging, move],
  );

  return {
    order,
    visible,
    hidden,
    isOpen: (id: PanelSectionId) => !collapsed.has(id),
    toggle,
    setHidden,
    nudge,
    reset,
    dragHandlers,
    density: prefs?.density,
    width: prefs?.width,
    pinned: prefs?.pinned ?? true,
  };
}
