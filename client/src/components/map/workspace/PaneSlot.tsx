// SPDX-License-Identifier: AGPL-3.0-or-later
import { useLayoutEffect, useRef } from 'react';

/**
 * Where a pane's element — made by `usePaneElement`, which says why — is shown. Taken in when
 * this mounts and given up when it unmounts; when a move unmounts one slot and mounts another in
 * the same commit, the old one lets go first, because React runs every removed component's layout
 * cleanup before any new one's layout effect. (`append` moves the element in either order.)
 */
export default function PaneSlot({ element, className }: { element: HTMLElement; className?: string }) {
  const ref = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const slot = ref.current;
    if (!slot) {
      return;
    }
    slot.append(element);
    return () => {
      if (element.parentElement === slot) {
        element.remove();
      }
    };
  }, [element]);
  return <div ref={ref} className={['workspace-pane-slot', className].filter(Boolean).join(' ')} />;
}
