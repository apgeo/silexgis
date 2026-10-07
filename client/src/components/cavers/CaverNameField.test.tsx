// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { caverReference, type CaverReferenceRow } from '../trips/roster.ts';

/**
 * The one text box that says who: somebody out of the directory, or a name it does not hold.
 *
 * What is asserted throughout is what the field would have a form *send* — an entry or a name —
 * read through the same function every form that names a person reads it by. That, and not the
 * text on screen, is what decides whether a club ends up with one entry for somebody or two.
 */

const { caversSpy, behind } = vi.hoisted(() => ({
  caversSpy: vi.fn(),
  // What the directory was last asked for, while that is not yet what is in the box.
  behind: { text: undefined as string | undefined },
}));

vi.mock('../../api/hooks.ts', () => ({
  useCavers: (search?: string) => caversSpy(search),
}));

// The directory is asked a little behind the typing. Here it is asked at once, so a test reads
// what was asked for without waiting on a timer — except in the one test that is about the
// moment in between, which says what the question in flight still is.
vi.mock('../../hooks/useDebouncedValue.ts', () => ({
  useDebouncedValue: (value: string) => behind.text ?? value,
}));

const { default: CaverNameField } = await import('./CaverNameField.tsx');

interface Person {
  id: string;
  name: string;
  cavingGroups: { cavingGroupId: string; name: string; role: string }[];
}

const person = (id: string, name: string, clubs: string[] = []): Person => ({
  id,
  name,
  cavingGroups: clubs.map((club, index) => ({
    cavingGroupId: `club-${index}`,
    name: club,
    role: 'member',
  })),
});

/** Holds the row the way a form does, and shows what that row would be written as. */
function Harness({ initial }: { initial?: CaverReferenceRow }) {
  const [row, setRow] = useState<CaverReferenceRow | undefined>(initial);
  return (
    <>
      <CaverNameField value={row} onChange={setRow} placeholder="Who" data-testid="who" />
      <output data-testid="written">{JSON.stringify(caverReference(row ?? { name: '' }))}</output>
    </>
  );
}

const box = () => screen.getByTestId('who') as HTMLInputElement;
const type = (text: string) => fireEvent.change(box(), { target: { value: text } });
const written = () =>
  JSON.parse(screen.getByTestId('written').textContent ?? '{}') as {
    caverId: string | null;
    newCaverName: string | null;
  };
const offered = (name: string) =>
  Array.from(document.querySelectorAll<HTMLElement>(`.ant-select-item-option[title="${name}"]`));

beforeEach(() => {
  behind.text = undefined;
  caversSpy.mockReset().mockReturnValue({ data: [person('caver-ana', 'Ana Pop')] });
});
afterEach(cleanup);

describe('saying who, in one text box', () => {
  it('sends text that was typed and not chosen as a name', () => {
    render(<Harness />);
    type('Vasile Bucătarul');

    expect(written()).toEqual({ caverId: null, newCaverName: 'Vasile Bucătarul' });
    // And says so, in words that state the rule rather than predict how it will come out: who a
    // typed name means is decided where the directory is, not here.
    expect(screen.getByTestId('caver-name-typed').textContent).toMatch(/saved as a name/);
    expect(screen.queryByTestId('caver-name-picked')).toBeNull();
  });

  it('names somebody chosen from the directory by their entry', () => {
    render(<Harness />);
    type('Ana');
    fireEvent.click(offered('Ana Pop')[0]);

    expect(written()).toEqual({ caverId: 'caver-ana', newCaverName: null });
    // The box shows the person's name, never the identifier the choice was keyed by.
    expect(box().value).toBe('Ana Pop');
    expect(screen.getByTestId('caver-name-picked')).toBeTruthy();
    expect(screen.queryByTestId('caver-name-typed')).toBeNull();
  });

  it('typing a name somebody holds is not choosing them', () => {
    // The text matches a person exactly and the row still carries no entry: the reference is
    // what a write is read by, and inventing one here would be this field deciding who a name
    // means. The name is sent as a name and the server reads it against the directory.
    render(<Harness />);
    type('Ana Pop');

    expect(written()).toEqual({ caverId: null, newCaverName: 'Ana Pop' });
    expect(screen.getByTestId('caver-name-typed')).toBeTruthy();
  });

  it('offers two people of one name as two choices, each their own', () => {
    // Two entries under one name is the state the directory's merge exists to resolve. A list
    // keyed by the text would hand back the same one of them whichever row was pressed.
    caversSpy.mockReturnValue({
      data: [
        person('caver-older', 'Ion Popescu', ['Silex Brașov']),
        person('caver-newer', 'Ion Popescu', ['Speo Cluj']),
      ],
    });
    render(<Harness />);
    type('Ion');

    expect(offered('Ion Popescu')).toHaveLength(2);
    // The clubs each belongs to are what tells the two rows apart at a glance.
    expect(offered('Ion Popescu')[0].textContent).toContain('Silex Brașov');
    expect(offered('Ion Popescu')[1].textContent).toContain('Speo Cluj');

    fireEvent.click(offered('Ion Popescu')[1]);
    expect(written()).toEqual({ caverId: 'caver-newer', newCaverName: null });

    type('Ion');
    fireEvent.click(offered('Ion Popescu')[0]);
    expect(written()).toEqual({ caverId: 'caver-older', newCaverName: null });
  });

  it('reads a choice out as the person, never as the identifier it is keyed by', () => {
    // The list hands assistive technology a row of its own for the choices in reach, and fills
    // it with the choice's key unless told what to call it. Keyed by the person, that row would
    // be read out as an identifier — so each choice says who it is, with the clubs that tell
    // two people of one name apart, exactly as the row on screen does.
    caversSpy.mockReturnValue({
      data: [
        person('caver-older', 'Ion Popescu', ['Silex Brașov', 'Speo Cluj']),
        person('caver-newer', 'Ion Popescu'),
      ],
    });
    render(<Harness />);
    type('Ion');

    const readOut = () =>
      Array.from(document.querySelectorAll('[role="option"]')).map((option) =>
        option.getAttribute('aria-label'),
      );
    expect(readOut()).toEqual(['Ion Popescu, Silex Brașov · Speo Cluj']);

    // One step down the list brings the second choice into reach, who belongs to no club: the
    // name, and nothing after it.
    fireEvent.keyDown(box(), { key: 'ArrowDown', keyCode: 40, which: 40 });
    expect(readOut()).toEqual(['Ion Popescu, Silex Brașov · Speo Cluj', 'Ion Popescu']);
  });

  it('means whoever the new text names once somebody chosen is typed over', () => {
    render(<Harness />);
    type('Ana');
    fireEvent.click(offered('Ana Pop')[0]);
    expect(written().caverId).toBe('caver-ana');

    type('Ana Popescu');

    // Keeping the entry beside a corrected name would write the old person under the new text.
    expect(written()).toEqual({ caverId: null, newCaverName: 'Ana Popescu' });
    expect(screen.getByTestId('caver-name-typed')).toBeTruthy();
  });

  it('still means the person a row was loaded for while the name is left alone', () => {
    // The name shown for somebody with an account is their own label, which need not be the
    // name the directory records — so a row opened and saved untouched must go back as the
    // entry it came with, or it would be written as a name and make a second person.
    render(
      <Harness initial={{ caverId: 'caver-1', loadedName: 'Ana P.', name: 'Ana P.' }} />,
    );

    expect(box().value).toBe('Ana P.');
    expect(written()).toEqual({ caverId: 'caver-1', newCaverName: null });
    expect(screen.getByTestId('caver-name-picked')).toBeTruthy();
  });

  it('asks the directory for what was typed, and for its first page while nothing is', () => {
    render(<Harness />);
    expect(caversSpy).toHaveBeenLastCalledWith(undefined);

    type('  Ana ');
    expect(caversSpy).toHaveBeenLastCalledWith('Ana');
  });

  it('offers what the directory found even where the letters typed do not match', () => {
    // The directory matches without regard to accents. Filtering its answer again by the letters
    // on screen would hide exactly the person it found that way.
    caversSpy.mockReturnValue({ data: [person('caver-stefan', 'Ștefan Munteanu')] });
    render(<Harness />);
    type('Stefan');

    expect(offered('Ștefan Munteanu')).toHaveLength(1);
    fireEvent.click(offered('Ștefan Munteanu')[0]);
    expect(written()).toEqual({ caverId: 'caver-stefan', newCaverName: null });
  });

  it('offers nobody while the answer in hand is to the text before', () => {
    // Between a keystroke and the question it leads to, the list in hand answers what the box
    // said a moment ago. Nothing here filters what the server found, so offering it would put
    // people who do not match at the top of a list that has stopped following the typing.
    caversSpy.mockReturnValue({ data: [person('caver-bogdan', 'Bogdan Ilie')] });
    behind.text = 'Bog';
    render(<Harness />);
    type('Ana');

    expect(caversSpy).toHaveBeenLastCalledWith('Bog');
    expect(offered('Bogdan Ilie')).toHaveLength(0);

    // The question catches up, and what is offered is its answer.
    behind.text = undefined;
    caversSpy.mockReturnValue({ data: [person('caver-ana', 'Ana Pop')] });
    type('Ana P');
    expect(caversSpy).toHaveBeenLastCalledWith('Ana P');
    expect(offered('Ana Pop')).toHaveLength(1);
    expect(offered('Bogdan Ilie')).toHaveLength(0);
  });

  it('says nothing under an empty box', () => {
    render(<Harness />);

    expect(screen.queryByTestId('caver-name-typed')).toBeNull();
    expect(screen.queryByTestId('caver-name-picked')).toBeNull();
  });

  it('stays a working text box when the directory cannot be read', () => {
    // A caller refused the directory gets no suggestions — and can still type a name, which is
    // all a record that names somebody needs from them.
    caversSpy.mockReturnValue({ data: undefined });
    render(<Harness />);
    type('Somebody New');

    expect(written()).toEqual({ caverId: null, newCaverName: 'Somebody New' });
  });
});
