// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type { ImportLithologyValue } from '../../api/hooks/imports.ts';
import LithologyMappingCard from './LithologyMappingCard.tsx';

afterEach(cleanup);

const values: ImportLithologyValue[] = [
  { value: 'Flis carpatic', count: 3, code: null, match: 'unmatched' },
  { value: 'Calcar', count: 12, code: 'limestone', match: 'recognised' },
];

describe('LithologyMappingCard', () => {
  it('draws nothing when the file has no rock column', () => {
    const { container } = render(<LithologyMappingCard values={[]} mapping={{}} onChange={() => {}} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('lists the values with what they become and says how many are unmatched', async () => {
    await i18n.changeLanguage('en');
    render(<LithologyMappingCard values={values} mapping={{}} onChange={() => {}} />);
    expect(screen.getByText('Flis carpatic')).toBeInTheDocument();
    expect(screen.getByText('No match')).toBeInTheDocument();
    expect(screen.getByText('Recognised')).toBeInTheDocument();
    expect(screen.getByText(/matching no class: 1/)).toBeInTheDocument();
    expect(screen.getByText('Limestone')).toBeInTheDocument();
  });

  it('records the reviewer choice and removes it again when cleared', async () => {
    await i18n.changeLanguage('en');
    const onChange = vi.fn();
    const { rerender } = render(<LithologyMappingCard values={values} mapping={{}} onChange={onChange} />);

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Class for Flis carpatic' }));
    fireEvent.click(await screen.findByTitle('Shale / marl'));
    expect(onChange).toHaveBeenLastCalledWith({ 'Flis carpatic': 'shale_marl' });

    rerender(
      <LithologyMappingCard
        values={[{ ...values[0], code: 'shale_marl', match: 'mapped' }, values[1]]}
        mapping={{ 'Flis carpatic': 'shale_marl' }}
        onChange={onChange}
      />,
    );
    expect(screen.getByText('Your choice')).toBeInTheDocument();
  });
});
