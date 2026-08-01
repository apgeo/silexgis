// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import i18next from 'i18next';
import { parseAccessActions } from '../../api/hooks.ts';
import AccessExplanationList from './AccessExplanationList.tsx';
import { actionSetLabels, explanationReason, joinActions } from './accessDisplay.ts';

const t = i18next.t.bind(i18next);

afterEach(cleanup);

describe('parseAccessActions', () => {
  it('splits the wire format and drops none', () => {
    expect([...parseAccessActions('read, write, viewExactLocation')]).toEqual([
      'read', 'write', 'viewExactLocation',
    ]);
    expect(parseAccessActions('none').size).toBe(0);
    expect(parseAccessActions(undefined).size).toBe(0);
    expect(parseAccessActions('').size).toBe(0);
  });

  it('round-trips through joinActions in display order', () => {
    expect(joinActions(parseAccessActions('write, read, create'))).toBe('read, write, create');
  });

  it('labels an action set in a stable order', () => {
    expect(actionSetLabels(t, 'write, read')).toEqual(['Read', 'Write']);
  });
});

describe('explanationReason', () => {
  it('names the deciding rule and its level', () => {
    expect(explanationReason(t, {
      allowed: false, source: 'entries', level: 'collection', ruleName: 'Sensitive areas', redacted: false,
    })).toBe('Decided at the collection level by "Sensitive areas".');
  });

  it('states a withheld anchor as a rule the caller cannot see — never an error, never blank', () => {
    const reason = explanationReason(t, {
      allowed: false, source: 'entries', level: 'global', ruleName: null, redacted: true,
    });
    expect(reason).toBe('Decided at the global level by a rule you cannot see.');
  });

  it('covers the built-in sources', () => {
    expect(explanationReason(t, { allowed: true, source: 'ownership', redacted: false }))
      .toBe('Granted by ownership of the object.');
    expect(explanationReason(t, { allowed: true, source: 'visibility', redacted: false }))
      .toBe("Granted by the object's visibility.");
    expect(explanationReason(t, { allowed: true, source: 'fullAdministrators', redacted: false }))
      .toBe('Full administration: membership decides before any rule is consulted.');
    expect(explanationReason(t, { allowed: false, source: 'defaultDeny', redacted: false }))
      .toBe('No rule grants this.');
  });
});

describe('AccessExplanationList', () => {
  it('renders a verdict tag and the redacted reason as a normal answer', () => {
    render(
      <AccessExplanationList
        explanations={[
          { action: 'read', allowed: true, source: 'visibility', level: null, ruleName: null, redacted: false },
          { action: 'write', allowed: false, source: 'entries', level: 'global', ruleName: null, redacted: true },
        ]}
      />,
    );

    expect(screen.getByText('Allowed')).toBeInTheDocument();
    expect(screen.getByText('Denied')).toBeInTheDocument();
    expect(screen.getByText('Decided at the global level by a rule you cannot see.')).toBeInTheDocument();
  });
});
