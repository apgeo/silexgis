// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import type { ResourceRef, ViewControlKind } from './resourceRef.ts';
import { registerViewControl } from './viewTargets.ts';

/**
 * Registers a view as somewhere links can be sent, for as long as it is mounted.
 *
 * The two callbacks ride refs. Registering announces on the bus, and a component that
 * re-registered whenever a closure changed identity would put a message on that bus on every
 * render of the map — which is most of them, since a map re-renders as it moves. What the
 * registration is keyed on is what other windows can actually see: the address, the kind and the
 * label.
 */
export function useViewControl(spec: {
  /** Stable within this window. A view mounted twice needs two of these. */
  id: string;
  kind: ViewControlKind;
  labelKey: string;
  canReveal: (ref: ResourceRef) => boolean;
  reveal: (ref: ResourceRef) => void;
  /** False while the view cannot answer at all — not yet loaded, not on screen. */
  enabled?: boolean;
}): void {
  const { id, kind, labelKey, enabled = true } = spec;

  const canRevealRef = useRef(spec.canReveal);
  canRevealRef.current = spec.canReveal;
  const revealRef = useRef(spec.reveal);
  revealRef.current = spec.reveal;

  useEffect(() => {
    if (!enabled) {
      return;
    }

    return registerViewControl({
      id,
      kind,
      labelKey,
      canReveal: (ref) => canRevealRef.current(ref),
      reveal: (ref) => revealRef.current(ref),
    });
  }, [id, kind, labelKey, enabled]);
}
