// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render as renderBare, screen } from '@testing-library/react';
import type { ReactElement } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import '../../i18n';
import PositionTextInput from './PositionTextInput.tsx';

// Both link to the converter, so they are rendered inside a router.
const render = (ui: ReactElement) => renderBare(ui, { wrapper: MemoryRouter });

afterEach(cleanup);

const type = (text: string) => fireEvent.change(screen.getByTestId('position-text-input'), { target: { value: text } });

it('reads degrees, minutes and seconds, says what it read, and hands on decimal degrees', () => {
  const onPosition = vi.fn();
  render(<PositionTextInput onPosition={onPosition} />);

  type('45°35′58″N 24°44′10″E');
  expect(screen.getByTestId('position-text-feedback').textContent).toContain('Read as Degrees, minutes, seconds');

  fireEvent.click(screen.getByRole('button', { name: 'Use' }));
  const [[lonLat]] = onPosition.mock.calls as [[number, number]][];
  expect(lonLat[0]).toBeCloseTo(24 + 44 / 60 + 10 / 3600, 6);
  expect(lonLat[1]).toBeCloseTo(45 + 35 / 60 + 58 / 3600, 6);
});

it('reads a UTM reference and takes it on Enter', () => {
  const onPosition = vi.fn();
  render(<PositionTextInput onPosition={onPosition} />);

  type('35T 401234 5049876');
  fireEvent.keyDown(screen.getByTestId('position-text-input'), { key: 'Enter', code: 'Enter', keyCode: 13 });

  expect(onPosition).toHaveBeenCalledTimes(1);
  const [lon, lat] = onPosition.mock.calls[0][0] as [number, number];
  expect(lon).toBeGreaterThan(24);
  expect(lon).toBeLessThan(26);
  expect(lat).toBeGreaterThan(45);
  expect(lat).toBeLessThan(46);
});

it('says why a text cannot be read, and offers nothing to use', () => {
  const onPosition = vi.fn();
  render(<PositionTextInput onPosition={onPosition} />);

  type('45°75′N 24°10′E');
  expect(screen.getByTestId('position-text-input').getAttribute('aria-invalid')).toBe('true');
  expect(screen.getByRole('button', { name: 'Use' })).toBeDisabled();
  expect(screen.getByTestId('position-text-feedback').textContent).toBe('Minutes must be below 60.');
  expect(onPosition).not.toHaveBeenCalled();
});

it('is empty and closed when the position beside it may not be edited', () => {
  render(<PositionTextInput onPosition={vi.fn()} disabled />);
  expect(screen.getByTestId('position-text-input')).toBeDisabled();
  expect((screen.getByTestId('position-text-input') as HTMLInputElement).value).toBe('');
});
