// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';

// The library's chooser needs a live map to draw its preview; what is under test is only that its
// opening button — drawn with the library's own class — ends up named.
vi.mock('@terrestris/react-geo/dist/BackgroundLayerChooser/BackgroundLayerChooser', () => ({
  default: () => <button type="button" className="change-bg-btn" />,
}));

const { default: BaseLayerChooser } = await import('./BaseLayerChooser.tsx');

afterEach(cleanup);

it("names the library's base-layer button", () => {
  render(<BaseLayerChooser layers={[]} />);

  expect(screen.getByRole('button', { name: 'Change the base layer' })).toBeTruthy();
});
