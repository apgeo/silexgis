// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ImportCandidate, ImportDecision, ImportOptions } from '../../api/hooks.ts';

vi.mock('../../api/hooks.ts', () => ({
  useCaveTypes: () => ({ data: [{ code: 'karst-cave', name: 'Sample cave type' }] }),
  useEntranceTypes: () => ({ data: [{ code: 'shaft', name: 'Sample shaft' }] }),
  useGenericFeatureTypes: () => ({ data: [{ code: 'spring', name: 'Sample spring' }] }),
}));

const { default: CandidateTable } = await import('./CandidateTable.tsx');

// Invented rows: placeholder names and round positions, nothing from a real register.
function candidate(sourceId: number, extra: Partial<ImportCandidate> = {}): ImportCandidate {
  return {
    sourceId,
    geometry: 'point',
    sourceName: `waypoint ${sourceId}`,
    sourceDescription: null,
    sourceCode: null,
    sourceElevation: null,
    ruleId: null,
    ruleName: null,
    conflictingRuleNames: [],
    proposedKind: 'cave',
    proposedCaveTypeCode: null,
    proposedEntranceTypeCode: null,
    proposedFeatureTypeCode: null,
    proposedName: `pestera ${sourceId}`,
    geom: { type: 'Point', coordinates: [22.5, 45.5] },
    duplicate: null,
    decision: null,
    ...extra,
  } as ImportCandidate;
}

interface Harness {
  items: ImportCandidate[];
  elevationPolicy?: ImportOptions['elevation'];
  selectableIds?: number[];
  initialSelected?: number[];
}

/** The page's part: it keeps the decisions and the selection and hands them back, as the workspace does. */
function renderTable({ items, elevationPolicy = 'perCandidate', selectableIds = [], initialSelected = [] }: Harness) {
  const decided: Array<{ sourceId: number; decision: ImportDecision | null }> = [];
  const selections: number[][] = [];
  function Review() {
    const [decisions, setDecisions] = useState<Record<string, ImportDecision>>({});
    const [selected, setSelected] = useState<ReadonlySet<number>>(new Set(initialSelected));
    return (
      <CandidateTable
        items={items}
        elevationPolicy={elevationPolicy}
        decisions={decisions}
        selected={selected}
        onSelectedChange={(next) => {
          selections.push([...next].sort((a, b) => a - b));
          setSelected(next);
        }}
        selectableIds={selectableIds}
        onDecision={(sourceId, decision) => {
          decided.push({ sourceId, decision });
          setDecisions((current) => {
            const next = { ...current };
            if (decision === null) delete next[String(sourceId)];
            else next[String(sourceId)] = decision;
            return next;
          });
        }}
        onFocus={vi.fn()}
        page={1}
        pageSize={25}
        total={items.length}
        onPageChange={vi.fn()}
      />
    );
  }
  render(<Review />);
  return { decided, selections, lastDecision: () => decided.at(-1) };
}

const row = (sourceId: number) => document.querySelector<HTMLElement>(`tr[data-row-key="${sourceId}"]`)!;
const rowCheckbox = (sourceId: number) =>
  row(sourceId).querySelector<HTMLInputElement>('.ant-table-selection-column input[type="checkbox"]')!;

afterEach(cleanup);

describe('the import review table', () => {
  it('lets a row be selected only once something says what it becomes', () => {
    const { selections } = renderTable({
      items: [candidate(1), candidate(2, { proposedKind: null })],
    });

    // A row a rule gave a kind is selectable…
    expect(rowCheckbox(1)).not.toBeDisabled();
    fireEvent.click(rowCheckbox(1));
    expect(selections.at(-1)).toEqual([1]);
    // …a row nothing proposed anything for is not, until the reviewer gives it a kind.
    expect(rowCheckbox(2)).toBeDisabled();
  });

  it('a kind chosen on a row with none makes it selectable, and clearing it takes that back', () => {
    const { lastDecision } = renderTable({ items: [candidate(2, { proposedKind: null })] });
    expect(rowCheckbox(2)).toBeDisabled();

    const kind = within(row(2)).getAllByRole('combobox')[0];
    fireEvent.mouseDown(kind);
    fireEvent.click(document.querySelector('.ant-select-item-option[title="Surface feature"]')!);

    expect(lastDecision()).toEqual({ sourceId: 2, decision: { kind: 'surfaceFeature' } });
    expect(rowCheckbox(2)).not.toBeDisabled();
  });

  it('a type chosen for a cave is filed as a cave type, and the other kinds of type are cleared', () => {
    const { lastDecision } = renderTable({
      items: [candidate(3, { proposedEntranceTypeCode: 'shaft' })],
    });

    const type = within(row(3)).getAllByRole('combobox')[1];
    fireEvent.mouseDown(type);
    // A cave row offers the cave types, not the entrance or feature ones.
    expect(document.querySelector('.ant-select-item-option[title="Sample shaft"]')).toBeNull();
    fireEvent.click(document.querySelector('.ant-select-item-option[title="Sample cave type"]')!);

    expect(lastDecision()).toEqual({
      sourceId: 3,
      decision: { kind: 'cave', caveTypeCode: 'karst-cave', entranceTypeCode: null, featureTypeCode: null },
    });
  });

  it('a row near something already in the register can be attached to it, made its second entrance, or reset', () => {
    const { decided } = renderTable({
      items: [
        candidate(4, {
          duplicate: {
            featureId: 'feature-near',
            name: 'Sample entrance',
            kind: 'caveEntrance',
            distanceMeters: 12.4,
            nameSimilarity: 0.8,
            caveFeatureId: 'cave-near',
            caveName: 'Sample cave',
          },
        }),
        candidate(5),
      ],
    });

    expect(within(row(4)).getByText(/Sample entrance/)).toHaveTextContent('12 m away');
    // A row with nothing near it says so, and offers nothing to attach to.
    expect(within(row(5)).queryByText('It is this one')).toBeNull();

    fireEvent.click(within(row(4)).getByText('It is this one'));
    expect(decided.at(-1)).toEqual({ sourceId: 4, decision: { action: 'attach', attachToFeatureId: 'feature-near' } });
    expect(within(row(4)).getByText('Already there')).toBeInTheDocument();

    fireEvent.click(within(row(4)).getByText('Second entrance of Sample cave'));
    expect(decided.at(-1)).toEqual({
      sourceId: 4,
      decision: { action: 'create', kind: 'caveEntrance', attachToFeatureId: 'cave-near' },
    });

    fireEvent.click(within(row(4)).getByText('It is this one'));
    fireEvent.click(within(row(4)).getByRole('button', { name: /Reset/ }));
    expect(decided.at(-1)).toEqual({ sourceId: 4, decision: null });
  });

  it("an altitude the file carries is kept or dropped by the import's policy, and a row may answer otherwise", () => {
    const { lastDecision } = renderTable({
      items: [candidate(6, { sourceElevation: 812.6 }), candidate(7)],
      elevationPolicy: 'keep',
    });

    const keep = within(row(6)).getByRole('checkbox', { name: /Keep 813 m/ });
    expect(keep).toBeChecked();
    // A row whose file gave no altitude has nothing to keep.
    expect(within(row(7)).queryByText(/Keep .* m/)).toBeNull();

    fireEvent.click(keep);
    expect(lastDecision()).toEqual({ sourceId: 6, decision: { keepElevation: false } });
    expect(within(row(6)).getByRole('checkbox', { name: /Keep 813 m/ })).not.toBeChecked();
  });

  it('says when several rules claimed a row, and a row no rule matched says that', () => {
    renderTable({
      items: [
        candidate(8, { ruleName: 'Rule A', conflictingRuleNames: ['Rule B'] }),
        candidate(9, { ruleName: 'Rule A' }),
        candidate(10),
      ],
    });

    expect(within(row(8)).getByText('Rule A')).toBeInTheDocument();
    expect(within(row(8)).getByText('Two rules claimed it')).toBeInTheDocument();
    expect(within(row(9)).queryByText('Two rules claimed it')).toBeNull();
    expect(within(row(10)).getByText('No rule claimed it')).toBeInTheDocument();
  });

  // The menu opens on hover after a short delay, so its entries are waited for rather than read at once.
  // Each case starts from a fresh table: a closed menu's hidden copy keeps what it was last drawn with,
  // and jsdom lets a test click that copy, which no person can.
  const openMenu = () => fireEvent.mouseEnter(document.querySelector('.ant-table-selection-extra span')!);

  it("the header menu's select-all takes every selectable row across the pages, not just this page's", async () => {
    const { selections } = renderTable({ items: [candidate(1), candidate(2)], selectableIds: [1, 2, 30, 31] });

    openMenu();
    fireEvent.click(await screen.findByText('Select all 4 across every page'));

    expect(selections.at(-1)).toEqual([1, 2, 30, 31]);
  });

  it("the header menu's invert flips the selectable rows on every page and leaves a selection outside them alone", async () => {
    const { selections } = renderTable({
      items: [candidate(1), candidate(2)],
      selectableIds: [1, 2, 30, 31],
      initialSelected: [2, 99],
    });

    openMenu();
    fireEvent.click(await screen.findByText('Invert across every page (4)'));

    expect(selections.at(-1)).toEqual([1, 30, 31, 99]);
  });

  it("the header menu's none clears the selection", async () => {
    const { selections } = renderTable({ items: [candidate(1)], selectableIds: [1, 30], initialSelected: [1, 30] });

    openMenu();
    fireEvent.click(await screen.findByText('None'));

    expect(selections.at(-1)).toEqual([]);
  });
});
