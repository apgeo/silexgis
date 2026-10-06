// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { MapConfig } from '../../api/hooks.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const MB = 1024 * 1024;

/** What the installation publishes about the walls in view; undefined while it is still loading. */
let mapConfig: Partial<MapConfig> | undefined;

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return { ...actual, useMapConfig: () => ({ data: mapConfig }) };
});

const { default: AdvancedSettingsPage } = await import('./AdvancedSettingsPage.tsx');

const zoom = () => screen.getByRole('spinbutton', { name: 'Show from zoom' });
const caves = () => screen.getByRole('spinbutton', { name: 'Caves at most' });
const megabytes = () => screen.getByRole('spinbutton', { name: 'Megabytes at most' });
const reset = () => screen.getByRole('button', { name: "Use this installation's defaults" });

/** The three personal values as the store holds them. */
function stored() {
  const { meshesInViewMinZoom, meshesInViewMaxCaves, meshesInViewMaxBytes } = useUiPrefsStore.getState();
  return { minZoom: meshesInViewMinZoom, maxCaves: meshesInViewMaxCaves, maxBytes: meshesInViewMaxBytes };
}

/** Types into a number field the way a person does: the text changes, then the field is left. */
function type(input: HTMLElement, text: string) {
  fireEvent.change(input, { target: { value: text } });
  fireEvent.blur(input);
}

beforeEach(() => {
  mapConfig = {
    meshesInViewMinZoom: 14,
    meshesInViewMaxCaves: 12,
    meshesInViewMaxBytes: 64 * MB,
    meshesInViewMaxCavesLimit: 60,
    meshesInViewMaxBytesLimit: 512 * MB,
  };
  useUiPrefsStore.getState().setMeshesInViewLimits({});
});

afterEach(() => {
  cleanup();
  useUiPrefsStore.getState().setMeshesInViewLimits({});
  localStorage.removeItem('silexgis.uiPrefs');
});

describe('the limits of the cave walls in the 3D view', () => {
  it('shows the installation’s defaults in place of values nobody has set', () => {
    render(<AdvancedSettingsPage />);

    expect(screen.getByText('Cave walls in the 3D view')).toBeInTheDocument();
    // Empty fields, with the number in force behind each: writing the default into the field
    // would turn "follow the installation" into a copy that stops following it.
    expect(zoom()).toHaveValue('');
    expect(caves()).toHaveValue('');
    expect(megabytes()).toHaveValue('');
    expect(zoom()).toHaveAttribute('placeholder', '14');
    expect(caves()).toHaveAttribute('placeholder', '12');
    expect(megabytes()).toHaveAttribute('placeholder', '64');
  });

  it('says that an empty field follows the installation, and how far the installation lets a value go', () => {
    render(<AdvancedSettingsPage />);

    expect(screen.getByTestId('advanced-walls-hint')).toHaveTextContent(
      "An empty field follows this installation's default, shown in grey. " +
        'This installation allows at most 60 caves and 512 MB.',
    );
    expect(screen.getByText(/stays in this browser only/)).toBeInTheDocument();
  });

  it('stores a typed value for this browser and leaves the other two following the installation', () => {
    render(<AdvancedSettingsPage />);

    type(caves(), '30');

    expect(stored()).toEqual({ minZoom: undefined, maxCaves: 30, maxBytes: undefined });
    expect(JSON.parse(localStorage.getItem('silexgis.uiPrefs')!).state.meshesInViewMaxCaves).toBe(30);

    type(zoom(), '12');

    expect(stored()).toEqual({ minZoom: 12, maxCaves: 30, maxBytes: undefined });
  });

  it('takes megabytes and stores bytes, and shows a stored budget back in megabytes', () => {
    render(<AdvancedSettingsPage />);

    type(megabytes(), '256');

    // Bytes are what the installation's own budget and every mesh's stated size are counted in.
    expect(stored().maxBytes).toBe(256 * MB);
    expect(megabytes()).toHaveValue('256');

    cleanup();
    useUiPrefsStore.getState().setMeshesInViewLimits({ maxBytes: 128 * MB });
    render(<AdvancedSettingsPage />);
    expect(megabytes()).toHaveValue('128');
  });

  it('stops each field at the installation’s ceiling, and the zoom at the zooms a map has', () => {
    render(<AdvancedSettingsPage />);

    expect(zoom()).toHaveAttribute('aria-valuemin', '1');
    expect(zoom()).toHaveAttribute('aria-valuemax', '22');
    expect(caves()).toHaveAttribute('aria-valuemin', '1');
    expect(caves()).toHaveAttribute('aria-valuemax', '60');
    expect(megabytes()).toHaveAttribute('aria-valuemin', '1');
    expect(megabytes()).toHaveAttribute('aria-valuemax', '512');

    // A number beyond a ceiling is brought back to it when the field is left, so what is stored
    // is what will be in force rather than a wish the scene then quietly cuts down.
    type(caves(), '500');
    expect(stored().maxCaves).toBe(60);

    type(megabytes(), '4096');
    expect(stored().maxBytes).toBe(512 * MB);

    type(zoom(), '0');
    expect(stored().minZoom).toBe(1);
  });

  it('follows a ceiling the installation has set lower, down to the whole megabyte below it', () => {
    mapConfig = { ...mapConfig, meshesInViewMaxCavesLimit: 20, meshesInViewMaxBytesLimit: 100 * MB + 1 };
    render(<AdvancedSettingsPage />);

    expect(caves()).toHaveAttribute('aria-valuemax', '20');
    expect(megabytes()).toHaveAttribute('aria-valuemax', '100');
  });

  it('hands all three back to the installation with one button', () => {
    useUiPrefsStore.getState().setMeshesInViewLimits({ minZoom: 12, maxCaves: 30, maxBytes: 256 * MB });
    render(<AdvancedSettingsPage />);
    expect(zoom()).toHaveValue('12');
    expect(caves()).toHaveValue('30');
    expect(megabytes()).toHaveValue('256');

    fireEvent.click(reset());

    expect(stored()).toEqual({ minZoom: undefined, maxCaves: undefined, maxBytes: undefined });
    // The fields are empty again, not still showing what was just given up.
    expect(zoom()).toHaveValue('');
    expect(caves()).toHaveValue('');
    expect(megabytes()).toHaveValue('');
    // And with nothing left to hand back, the button says so by being unavailable.
    expect(reset()).toBeDisabled();
  });

  it('clears one value when its field is emptied, without touching the others', () => {
    useUiPrefsStore.getState().setMeshesInViewLimits({ minZoom: 12, maxCaves: 30, maxBytes: 256 * MB });
    render(<AdvancedSettingsPage />);

    type(caves(), '');

    expect(stored()).toEqual({ minZoom: 12, maxCaves: undefined, maxBytes: 256 * MB });
  });

  it('still lets a value be typed before the installation’s limits have arrived', () => {
    mapConfig = undefined;
    render(<AdvancedSettingsPage />);

    expect(caves()).toHaveAttribute('placeholder', '');
    expect(screen.getByTestId('advanced-walls-hint')).toHaveTextContent(
      "An empty field follows this installation's default, shown in grey.",
    );

    type(caves(), '30');
    expect(stored().maxCaves).toBe(30);
  });
});
