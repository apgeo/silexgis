// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { ConfigProvider, theme } from 'antd';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import RuleHitList from './RuleHitList.tsx';

afterEach(cleanup);

/** A colour as the browser reports it back from an inline style. */
function rendered(color: string): string {
  const probe = document.createElement('span');
  probe.style.color = color;
  return probe.style.color;
}

describe('the rule hit list', () => {
  it('draws its chips and the picked line in the colours of the theme in force, dark included', () => {
    const dark = theme.getDesignToken({ algorithm: theme.darkAlgorithm });
    const { container } = render(
      <ConfigProvider theme={{ algorithm: theme.darkAlgorithm }}>
        <RuleHitList
          hits={[
            { ruleId: 'r1', ruleName: 'Invented rule', count: 3 },
            { ruleId: 'r2', ruleName: 'Rule that claimed nothing', count: 0 },
          ]}
          unmatched={0}
          activeRule="r1"
          onPick={() => {}}
        />
      </ConfigProvider>,
    );
    const counts = Array.from(container.querySelectorAll<HTMLElement>('.ant-badge-count'));
    expect(counts).toHaveLength(3);
    // A zero is a quiet chip: the theme's fill and secondary text, not a light grey left on a dark card.
    expect(counts[1].style.background).toBe(rendered(dark.colorFillSecondary));
    expect(counts[1].style.color).toBe(rendered(dark.colorTextSecondary));
    expect(counts[2].style.background).toBe(rendered(dark.colorFillSecondary));
    const picked = screen.getByText('Invented rule').closest<HTMLElement>('[role="button"]')!;
    expect(picked.style.background).toBe(rendered(dark.colorPrimaryBg));
  });
});
