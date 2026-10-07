// SPDX-License-Identifier: AGPL-3.0-or-later
import { AutoComplete, Flex, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCavers } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { referencesLoadedCaver, type CaverReferenceRow } from '../trips/roster.ts';

export interface CaverNameFieldProps {
  /**
   * The person as the form holds them: the text on screen, and the directory entry that text
   * was picked or loaded for, if it was. Absent is a field nobody has typed in yet.
   */
  value?: CaverReferenceRow;
  onChange?: (value: CaverReferenceRow) => void;
  /** Handed down by a form item so its label is the field's label. */
  id?: string;
  placeholder?: string;
  disabled?: boolean;
  /** A mark for a test to find the text box by. */
  'data-testid'?: string;
}

/**
 * Says who: somebody out of the club's directory, or a name the directory does not hold yet.
 *
 * One text box does both, because whoever is writing a record down does not know in advance
 * which of the two the person is — they know a name. Typing narrows the directory and offers
 * who it finds; choosing one of them names that person, by their entry. Text that was typed and
 * not chosen is a name and nothing more, and is sent as one.
 *
 * <b>This field never decides who a typed name means.</b> The server does, by one rule for every
 * record that may name a person this way: a name somebody is already recorded under means that
 * person, and only a name nobody holds adds them. Working that out here as well — "this looks
 * new", "this matches" — would be a second copy of a rule about identity, drawn from a list the
 * server cut short and matched more loosely than it resolves, and free to disagree with the
 * answer the save then gives. So the sentence under the box says what the rule is, and makes no
 * claim about how it will come out for the text in it.
 *
 * Which of the two a row is, is read where the row is written, by the same function wherever a
 * form lets somebody edit the text beside a person it already knows: a row still reading the
 * name it was picked or loaded under still means that person, and one typed over means whoever
 * the new text names.
 */
export default function CaverNameField({
  value,
  onChange,
  id,
  placeholder,
  disabled,
  'data-testid': testId,
}: CaverNameFieldProps) {
  const { t } = useTranslation();
  const row: CaverReferenceRow = value ?? { name: '' };

  // Asked of the server, a little behind the typing: it matches without regard to accents and
  // returns the first page of the directory for an empty box, which is what makes the box worth
  // opening before a letter is typed.
  const asked = row.name.trim();
  const search = useDebouncedValue(asked);
  const { data: cavers } = useCavers(search || undefined);
  // Only the answer to what is in the box now is offered. Between a keystroke and the question
  // it leads to, the answer in hand belongs to the text before it, and nothing here filters
  // what the server found — so offering it would show a list that has stopped following the
  // typing, with the wrong people at the top of it.
  const answered = search === asked ? (cavers ?? []) : [];

  const picked = referencesLoadedCaver(row);
  const typed = !picked && asked.length > 0;

  return (
    <Flex vertical gap={4}>
      <AutoComplete
        id={id}
        value={row.name}
        disabled={disabled}
        // Keyed by the person, never by their name. Two people can be written down under one
        // name, and a list keyed by the text hands back the same one of them whichever row is
        // pressed — so one of the two could not be chosen at all, with nothing to say so.
        options={answered.map((caver) => {
          // The clubs somebody belongs to, where they belong to any: the one thing on a
          // directory entry that tells two people of one name apart at a glance.
          const clubs = caver.cavingGroups.map((group) => group.name).join(' · ');
          return {
            value: caver.id,
            name: caver.name,
            title: caver.name,
            // What a screen reader calls the choice. The list reads a choice out by its value
            // unless it is given a name to use, and the value here is the person's identifier —
            // so without this, moving through the list would read out a string of hex digits
            // for each person. Said with the clubs, because whoever cannot see the second line
            // of the row needs it just as much to tell two people of one name apart.
            'aria-label': clubs ? `${caver.name}, ${clubs}` : caver.name,
            label: (
              <Flex vertical>
                <span>{caver.name}</span>
                {clubs && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {clubs}
                  </Typography.Text>
                )}
              </Flex>
            ),
          };
        })}
        // The server has already done the matching. Filtering its answer again by the letters
        // typed would hide exactly the rows it found by ignoring an accent.
        filterOption={false}
        notFoundContent={null}
        // Typing keeps whatever entry the row was holding beside the new text. Whether the text
        // still means that person is decided when the row is written, not on each keystroke:
        // somebody who retypes the name they picked has not picked anybody else.
        onSearch={(text) => onChange?.({ ...row, name: text })}
        onSelect={(caverId, option) =>
          onChange?.({ caverId: String(caverId), loadedName: option.name, name: option.name })
        }
      >
        {/* Drawn as a plain text box rather than left to the suggestion control's own, so the
            text on screen is the text the form reads. As long as a name the directory can hold. */}
        <Input data-testid={testId} placeholder={placeholder} maxLength={200} />
      </AutoComplete>
      {picked && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="caver-name-picked">
          {t('cavers.picker.picked')}
        </Typography.Text>
      )}
      {typed && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="caver-name-typed">
          {t('cavers.picker.typed')}
        </Typography.Text>
      )}
    </Flex>
  );
}
