// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import Scene3DLoading, { SLOW_AFTER_MS } from './Scene3DLoading.tsx';

beforeEach(async () => {
  await i18n.changeLanguage('en');
  vi.useFakeTimers();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

/**
 * A 3D scene drawn in software can take most of a minute to appear. A bare spinner over a black
 * box for that long reads as a scene that will never come; this says what is being waited for, and
 * once it has been a while, why it may be slow.
 */
describe('the loader over a 3D scene being built', () => {
  it('says what is loading', () => {
    render(<Scene3DLoading />);
    expect(screen.getByTestId('scene3d-loading')).toHaveTextContent('Loading the 3D scene…');
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(screen.queryByTestId('scene3d-loading-slow')).toBeNull();
  });

  it('says why it may be slow once it has been a while', () => {
    render(<Scene3DLoading />);
    act(() => {
      vi.advanceTimersByTime(SLOW_AFTER_MS);
    });
    expect(screen.getByTestId('scene3d-loading-slow')).toHaveTextContent('graphics');
  });
});
