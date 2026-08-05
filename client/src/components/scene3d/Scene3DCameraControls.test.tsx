// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import Scene3DCameraControls, { type Scene3DCameraControlsProps } from './Scene3DCameraControls.tsx';

// The two hooks that decide which layout this component draws. Mocked rather than driven by a
// viewport size and a media query, which is how the rest of this application tests its phone
// layouts.
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: vi.fn(() => false) }));
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: vi.fn(() => false) }));

beforeEach(() => {
  vi.mocked(useIsMobile).mockReturnValue(false);
  vi.mocked(useCoarsePointer).mockReturnValue(false);
});

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

describe('Scene3DCameraControls under a finger on a wide screen', () => {
  beforeEach(() => {
    // A phone turned sideways, or a tablet: wide enough for the desktop layout, and with no
    // hovering pointer to open a tooltip or the precision to hit a twenty-four-pixel button.
    vi.mocked(useIsMobile).mockReturnValue(false);
    vi.mocked(useCoarsePointer).mockReturnValue(true);
  });

  it('folds the strip away rather than offering seven glyphs to a finger', () => {
    renderControls();

    // Out on the scene each of those seven is a single letter whose only caption is a tooltip, and
    // a tooltip opens on hover — which this viewer does not have. Width alone would give them the
    // strip, because their screen is wide.
    expect(screen.getByTestId('scene3d-camera-trigger')).toBeInTheDocument();
    expect(screen.queryByTestId('scene3d-preset-north')).toBeNull();
  });

  it('captions every control in words there too', () => {
    renderControls();

    fireEvent.click(screen.getByTestId('scene3d-camera-trigger'));

    expect(screen.getByTestId('scene3d-preset-north')).toHaveTextContent('View from the north');
    expect(screen.getByTestId('scene3d-projection-toggle')).not.toHaveTextContent('');
  });
});

describe('Scene3DCameraControls at phone width', () => {
  beforeEach(() => {
    // Width is one of the two axes this component branches on, and the environment resolves it
    // through antd's responsive observer, so it is mocked rather than driven by a viewport size.
    vi.mocked(useIsMobile).mockReturnValue(true);
  });

  it('leaves one button over the scene instead of a column of seven', () => {
    renderControls();

    // Seven small buttons down the right edge take a quarter of a phone's height and stand over
    // the very thing they control.
    expect(screen.getByTestId('scene3d-camera-trigger')).toBeInTheDocument();
    expect(screen.queryByTestId('scene3d-preset-north')).toBeNull();
  });

  it('captions every control in words, because a finger cannot hover to read a tooltip', () => {
    renderControls();

    fireEvent.click(screen.getByTestId('scene3d-camera-trigger'));

    // On a touch device the tooltips explaining the glyphs never appear at all, so out on the
    // scene the strip would be seven unlabelled buttons. In the panel they are captioned.
    expect(screen.getByTestId('scene3d-preset-north')).toHaveTextContent('View from the north');
    expect(screen.getByTestId('scene3d-fit-cave')).toHaveTextContent('Frame the cave in view');
  });

  it('drives the same camera the desktop strip does', () => {
    const props = renderControls();

    fireEvent.click(screen.getByTestId('scene3d-camera-trigger'));
    fireEvent.click(screen.getByTestId('scene3d-preset-west'));
    expect(props.onPreset).toHaveBeenCalledWith('west');

    fireEvent.click(screen.getByTestId('scene3d-projection-toggle'));
    expect(props.onProjectionChange).toHaveBeenCalledWith('orthographic');
  });

  it('still refuses to frame a cave that is not drawn', () => {
    const props = renderControls({ fitDisabled: true });

    fireEvent.click(screen.getByTestId('scene3d-camera-trigger'));
    expect(screen.getByTestId('scene3d-fit-cave')).toBeDisabled();
    fireEvent.click(screen.getByTestId('scene3d-fit-cave'));
    expect(props.onFitCave).not.toHaveBeenCalled();
  });
});
