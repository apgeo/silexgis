// SPDX-License-Identifier: AGPL-3.0-or-later
import { Select, Typography } from 'antd';

/**
 * The control a list is grouped by: a few words saying what it does, then the things the list
 * can be grouped by.
 *
 * One component because more than one list offers grouping, and a reader who has learnt the
 * control on one of them should meet the same control on the next — the same words in front of
 * it, the same width, the same place for "nothing". What each list can be grouped by, and what
 * it does about the choice, stay with the list; only how the question is asked is shared.
 *
 * It draws the words and the select side by side and leaves the spacing to whatever row it is
 * put in, so that two of them in a row ("group by … then by …") read as one sentence.
 */
export interface GroupBySelectProps<T extends string> {
  /** The words in front of the control. */
  label: string;
  value: T;
  /**
   * What the list can be grouped by. Not grouping at all goes first: it is the ordinary state,
   * and a control whose first option reshapes the page reshapes it by accident.
   */
  options: readonly { value: T; label: string }[];
  onChange: (value: T) => void;
  disabled?: boolean;
  'data-testid'?: string;
}

export default function GroupBySelect<T extends string>({
  label,
  value,
  options,
  onChange,
  disabled,
  'data-testid': testId,
}: GroupBySelectProps<T>) {
  return (
    <>
      <Typography.Text type="secondary">{label}</Typography.Text>
      <Select<T>
        style={{ minWidth: 160 }}
        data-testid={testId}
        value={value}
        disabled={disabled}
        options={[...options]}
        // The choice and nothing else: the select hands its listener the chosen option as well,
        // and a caller that passed something taking a second argument would be handed it.
        onChange={(next) => onChange(next)}
      />
    </>
  );
}
