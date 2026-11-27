// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { PhotoImportOptions } from '../../api/hooks.ts';
import { expectEveryFieldNamed } from '../../testFieldNames.ts';

vi.mock('../../api/hooks.ts', () => ({
  useCavingGroups: () => ({ data: [{ id: 'g-1', name: 'Invented club' }], isLoading: false }),
  usePhotoImportTracks: () => ({ data: [], isLoading: false }),
}));

const { default: PhotoImportOptionsPanel } = await import('./PhotoImportOptionsPanel.tsx');

beforeAll(async () => {
  await i18n.changeLanguage('en');
});
afterEach(cleanup);

describe('the photo review options for a screen reader', () => {
  it('names every field and both distance sliders, in every section', () => {
    const { container } = render(
      <PhotoImportOptionsPanel options={{} as PhotoImportOptions} onChange={() => {}} />,
    );
    // Only the first section is open at the start; open the other two so their fields are drawn.
    fireEvent.click(screen.getByText('What gets created'));
    fireEvent.click(screen.getByText('Pictures with no position'));

    expectEveryFieldNamed(container);
    const sliders = screen.getAllByRole('slider');
    expect(sliders).toHaveLength(2);
    expect(sliders[0]).toHaveAccessibleName('Two pictures are the same place within');
    expect(sliders[1]).toHaveAccessibleName('Look for something already there within');
  });
});
