// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CavePattern } from '../../api/hooks.ts';

const { patternSpy } = vi.hoisted(() => ({ patternSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useCavePattern: (...args: unknown[]) => patternSpy(...args),
}));

const { default: CavePatternPanel } = await import('./CavePatternPanel.tsx');

const maze: CavePattern = {
  caveId: 'cave-1',
  basis: 'surveyFlags',
  isApproximation: false,
  surveyModelId: 'model-1',
  hasAltitudes: true,
  network: {
    nodeCount: 16,
    edgeCount: 24,
    componentCount: 1,
    reducedNodeCount: 16,
    cyclomaticNumber: 9,
    extremityCount: 0,
    clustering: 0.31,
  },
  suggestion: {
    pattern: 'angularMaze',
    basis: 'surveyFlags',
    isApproximation: false,
    scores: [
      { kind: 'angularMaze', score: 3.5 },
      { kind: 'vadoseBranchwork', score: 0 },
    ],
    rules: [
      {
        rule: 'networkIsLooped',
        outcome: 'fired',
        supports: ['angularMaze', 'looping'],
        weight: 2,
        figures: [{ figure: 'loopsPerNode', value: 0.5625 }],
      },
      {
        rule: 'networkIsTreeLike',
        outcome: 'didNotFire',
        supports: ['vadoseBranchwork', 'waterTable'],
        weight: 1.5,
        figures: [{ figure: 'loopsPerNode', value: 0.5625 }],
      },
      {
        rule: 'sectionIsWide',
        outcome: 'notAssessable',
        supports: ['waterTable'],
        weight: 1,
        figures: [{ figure: 'medianWidthHeightRatio', value: null }],
      },
    ],
    assessableRuleCount: 2,
    firedRuleCount: 1,
    caveats: ['noCrossSections'],
  },
};

function show(data: CavePattern | undefined, { isError = false, isLoading = false } = {}) {
  patternSpy.mockReturnValue({ data, isLoading, isError });
  return render(
    <App>
      <CavePatternPanel caveId="cave-1" />
    </App>,
  );
}

afterEach(cleanup);

describe('CavePatternPanel', () => {
  it('names the suggestion', () => {
    show(maze);
    expect(screen.getByTestId('cave-pattern-label').textContent).toBe('Angular maze');
  });

  it('shows the reasoning without anything having to be opened', () => {
    show(maze);
    // The trace is the deliverable. It is in the card body, not behind a disclosure, so it is
    // present the moment the panel renders.
    expect(screen.getByTestId('cave-pattern-rules')).toBeTruthy();
    expect(screen.getByText('The network closes on itself often')).toBeTruthy();
    // Both loop rules read the same figure, and both report it: the figure a rule fired on
    // is what makes the rule checkable.
    expect(screen.getAllByText(/Loops per junction: 0.56/).length).toBe(2);
  });

  it('lists the rules that stayed silent and those that had nothing to read', () => {
    show(maze);
    // A rule never shown staying silent is not a rule, it is a constant.
    expect(screen.getByText('The network barely closes on itself')).toBeTruthy();
    expect(screen.getByText('Did not fire')).toBeTruthy();
    expect(screen.getByText('Nothing to read')).toBeTruthy();
    expect(screen.getByText(/1 of 2 rules that could be assessed fired/)).toBeTruthy();
  });

  it('says the suggestion is a proposal rather than a statement about the cave', () => {
    show(maze);
    expect(screen.getByText(/It is a proposal about this survey/)).toBeTruthy();
  });

  it('warns when the figures were approximated rather than taken from the survey flags', () => {
    show({
      ...maze,
      basis: 'skeletonHeuristic',
      isApproximation: true,
      surveyModelId: null,
      suggestion: {
        ...maze.suggestion,
        basis: 'skeletonHeuristic',
        isApproximation: true,
        caveats: ['figuresAreApproximated'],
      },
    });
    expect(screen.getByText(/rests on a guess for a measurement/)).toBeTruthy();
    expect(screen.getByText(/Approximated from the stored centerline/)).toBeTruthy();
  });

  it('is absent, not blank, when the server refuses', () => {
    const { container } = show(undefined, { isError: true });
    expect(container.textContent).toBe('');
  });
});
