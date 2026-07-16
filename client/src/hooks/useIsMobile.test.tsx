// SPDX-License-Identifier: AGPL-3.0-or-later
import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useIsMobile } from './useIsMobile.ts';

describe('useIsMobile', () => {
  // The guard that matters for the whole suite. antd resolves breakpoints through
  // matchMedia, which jsdom answers `false` to for every query unless the setup stub
  // evaluates it — and an unevaluated `(min-width: 768px)` reads as "not desktop", which
  // would quietly render the phone layout in every component test that does not mock this
  // hook. If this fails, the setupTests stub has stopped answering width queries.
  it('reports desktop at the default test viewport', () => {
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });

  // The mobile branch is deliberately not tested here: antd's responsive observer is a
  // module-level singleton that resolves its media queries once per module registry, so a
  // second case in this file would read the first case's cached screens whatever
  // window.innerWidth said. Components take the mobile path with this hook mocked
  // (EditToolbar, AppLayout, DialogHost), and the real breakpoint is exercised end to end
  // by the mobile-android Playwright project at a true 393px viewport.
});
