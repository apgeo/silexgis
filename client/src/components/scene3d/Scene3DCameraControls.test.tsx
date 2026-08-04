// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import Scene3DCameraControls, { type Scene3DCameraControlsProps } from './Scene3DCameraControls.tsx';

function renderControls(overrides: Partial<Scene3DCameraControlsProps> = {}) {
  const props: Scene3DCameraControlsProps = {
    activePreset: undefined,
    onPreset: vi.fn(),
    projection: 'perspective',
    onProjectionChange: vi.fn(),
    onFitCave: vi.fn(),
    fitDisabled: false,
    ...overrides,
  };
  render(<Scene3DCameraControls {...props} />);
  return props;
}

afterEach(cleanup);

describe('Scene3DCameraControls', () => {
  it('offers the plan and the four elevations, each in one press', () => {
    const props = renderControls();

    fireEvent.click(screen.getByRole('button', { name: 'View from the east' }));

    expect(props.onPreset).toHaveBeenCalledWith('east');
    for (const name of [
      'Look straight down (plan)',
      'View from the north',
      'View from the south',
      'View from the east',
      'View from the west',
    ]) {
      expect(screen.getByRole('button', { name })).toBeInTheDocument();
    }
  });

  it('writes the compass letters in the reader\'s own language', async () => {
    // A compass point is not the same letter everywhere: west is V in Romanian, so a hardcoded
    // "W" puts a letter on the button that stands for no direction the reader recognises.
    const { default: i18n } = await import('../../i18n');
    await i18n.changeLanguage('ro');
    try {
      renderControls();

      expect(screen.getByTestId('scene3d-preset-west')).toHaveTextContent('V');
      expect(screen.getByTestId('scene3d-preset-north')).toHaveTextContent('N');
    } finally {
      await i18n.changeLanguage('en');
    }
  });

  it('shows which preset the camera is at, and only that one', () => {
    // Read from the camera by whoever mounts this rather than remembered here: a preset places the
    // camera and lets go, so the highlight has to go out as soon as the viewer drags.
    renderControls({ activePreset: 'north' });

    expect(screen.getByTestId('scene3d-preset-north')).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('scene3d-preset-south')).toHaveAttribute('aria-pressed', 'false');
  });

  it('presses the same preset again as a fresh instruction rather than a toggle', () => {
    const props = renderControls({ activePreset: 'top' });

    fireEvent.click(screen.getByTestId('scene3d-preset-top'));

    expect(props.onPreset).toHaveBeenCalledWith('top');
  });

  it('switches the projection each way', () => {
    const off = renderControls({ projection: 'perspective' });
    fireEvent.click(screen.getByTestId('scene3d-projection-toggle'));
    expect(off.onProjectionChange).toHaveBeenCalledWith('orthographic');

    cleanup();
    const on = renderControls({ projection: 'orthographic' });
    expect(screen.getByTestId('scene3d-projection-toggle')).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(screen.getByTestId('scene3d-projection-toggle'));
    expect(on.onProjectionChange).toHaveBeenCalledWith('perspective');
  });

  it('offers nothing to frame when there is no cave drawn', () => {
    const props = renderControls({ fitDisabled: true });

    expect(screen.getByTestId('scene3d-fit-cave')).toBeDisabled();
    fireEvent.click(screen.getByTestId('scene3d-fit-cave'));
    expect(props.onFitCave).not.toHaveBeenCalled();
  });
});
