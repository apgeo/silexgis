// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import type { ActivityState } from '../../api/hooks.ts';
import TripStateTag from './TripStateTag.tsx';

afterEach(cleanup);

describe('TripStateTag', () => {
  it('names the state in words rather than showing the wire value', () => {
    render(<TripStateTag state="published" />);

    const tag = screen.getByTestId('trip-state');
    expect(tag).toHaveTextContent('Published');
    // The lookup key itself reaching the screen is the failure this guards against.
    expect(tag).not.toHaveTextContent('stateValues');
  });

  it('marks a draft with more than a colour', () => {
    const { unmount } = render(<TripStateTag state="draft" />);
    // A grey tag among other grey tags is not a signal; the icon is what makes an
    // unannounced trip readable at a glance in a list of them.
    expect(screen.getByTestId('trip-state').querySelector('.anticon')).not.toBeNull();
    unmount();

    render(<TripStateTag state="done" />);
    expect(screen.getByTestId('trip-state').querySelector('.anticon')).toBeNull();
  });

  it('renders every state in the shared vocabulary, including the ones no trip holds yet', () => {
    // The badge is fed straight from the server's value, and the vocabulary is shared with
    // activities that are not trips. A state with no colour or no label must not reach a
    // screen as a blank tag or a raw key.
    const states: ActivityState[] = [
      'draft',
      'proposed',
      'planned',
      'confirmed',
      'done',
      'published',
      'cancelled',
      'delayed',
    ];

    for (const state of states) {
      const { unmount } = render(<TripStateTag state={state} />);
      const text = screen.getByTestId('trip-state').textContent ?? '';
      expect(text.trim().length, state).toBeGreaterThan(0);
      expect(text, state).not.toContain('trips.');
      unmount();
    }
  });
});
