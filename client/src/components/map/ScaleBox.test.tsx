// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import MapUtil from '@terrestris/ol-util/dist/MapUtil/MapUtil';
import Map from 'ol/Map';
import View from 'ol/View';
import { fromLonLat, getPointResolution } from 'ol/proj';
import { afterEach, expect, it } from 'vitest';
import '../../i18n';
import ScaleBox from './ScaleBox.tsx';

afterEach(cleanup);

function setUp(center = [0, 0]) {
  const map = new Map({ view: new View({ center, zoom: 10 }) });
  render(<ScaleBox map={map} />);
  return { view: map.getView(), box: screen.getByRole('combobox', { name: 'Map scale' }) };
}

it('goes to a scale typed in any of the ways people write one', async () => {
  const { view, box } = setUp();

  fireEvent.change(box, { target: { value: '1:25 000' } });
  fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', keyCode: 13 });

  await waitFor(() => expect(view.getResolution()).toBeCloseTo(MapUtil.getResolutionForScale(25000, 'm')!, 6));
  expect(screen.queryByRole('alert')).toBeNull();
});

it('says what is wrong with text that is not a scale, and leaves the map where it was', async () => {
  const { view, box } = setUp();
  const before = view.getResolution();

  fireEvent.change(box, { target: { value: 'twenty' } });
  fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', keyCode: 13 });

  expect(await screen.findByRole('alert')).toHaveTextContent(/scale/i);
  expect(box).toHaveAttribute('aria-invalid', 'true');
  expect(view.getResolution()).toBe(before);
});

it('shows the scale the map is at', () => {
  const { view, box } = setUp();
  const expected = MapUtil.roundScale(MapUtil.getScaleForResolution(view.getResolution()!, 'm')!);
  expect((box as HTMLInputElement).value).toBe(`1:${expected.toLocaleString('en')}`);
});

it('works on the ground away from the equator: a typed 1:25000 is 1:25000 there', async () => {
  // At 45°N a Web Mercator metre is about 0.71 of a ground metre; the nominal scale would be off by that.
  const centre = fromLonLat([25, 45]);
  const { view, box } = setUp(centre);

  fireEvent.change(box, { target: { value: '1:25000' } });
  fireEvent.keyDown(box, { key: 'Enter', code: 'Enter', keyCode: 13 });

  await waitFor(() => {
    const ground = getPointResolution(view.getProjection(), view.getResolution()!, centre, 'm');
    expect(MapUtil.getScaleForResolution(ground, 'm')).toBeCloseTo(25000, 0);
  });
  expect((box as HTMLInputElement).value).toBe('1:25,000');
});

