// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CSSProperties } from 'react';
import { AutoComplete, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSurveyModelStationSearch } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/** The value of the row that says there are more names than the list shows. Never a station. */
const MORE_ROW = '\u0000more';

export interface SurveyStationInputProps {
  /**
   * The survey whose stations are offered, or nothing where there is none to offer from — no
   * survey chosen, or one this reader is not told about. The field is then a plain text box.
   */
  surveyModelId: string | null | undefined;
  value?: string;
  onChange?: (value: string) => void;
  /** Given by the form item around this, which is what ties its label to the box. */
  id?: string;
  style?: CSSProperties;
  'data-testid'?: string;
  'aria-required'?: boolean | 'true' | 'false';
  'aria-invalid'?: boolean | 'true' | 'false';
  'aria-describedby'?: string;
}

/**
 * A station's name, typed — with the survey's own stations offered underneath as it is typed.
 *
 * <b>Why offer them.</b> A station name is typed from memory or off a relayed message, exactly as
 * the survey spells it, and the first sign of a slip used to be the server refusing the whole
 * report. Offering the names that begin with what has been typed turns that into choosing.
 *
 * <b>Any text is still taken.</b> The list is a convenience and the server is the judge: a name
 * the list does not hold may be one the survey gained a minute ago, and a reader who may not be
 * told the survey's stations is offered none and must still be able to write down what they were
 * told. So nothing typed here is ever refused by the field itself.
 *
 * <b>The names are the ones the drawing shows,</b> because that is the spelling a report and a
 * declared place keep: what somebody picks here is what they will then read on the model and in
 * the log.
 *
 * The server does the narrowing — it folds capitals and accents and reads a name with or without
 * the file's root survey in front — so the list is shown as it arrives and never filtered again
 * here, which would hide matches the server found on purpose.
 */
export default function SurveyStationInput({
  surveyModelId,
  value,
  onChange,
  id,
  style,
  'data-testid': testId,
  ...aria
}: SurveyStationInputProps) {
  const { t } = useTranslation();
  const typed = (value ?? '').trim();
  const asked = useDebouncedValue(typed);
  const search = useSurveyModelStationSearch(surveyModelId ?? undefined, asked);

  // Only an answer to what is in the box now. While the box is ahead of the question the last
  // answer is still the best list there is, so it stays; an emptied box is offered nothing.
  const found = typed.length > 0 && asked.length > 0 ? (search.data?.items ?? []) : [];
  const names = [...new Set(found.map((station) => station.viewerName))];
  // One name, and it is the one already written: there is nothing left to choose, and a list
  // hanging open over the fields below would only be in the way of the next press.
  const settled = names.length === 1 && names[0] === typed;
  const more = (search.data?.totalItems ?? 0) - found.length;

  const options = settled
    ? []
    : [
        ...names.map((name) => ({ value: name, label: name })),
        ...(names.length > 0 && more > 0
          ? [
              {
                value: MORE_ROW,
                disabled: true,
                label: t('surveyModels.stationSearchMore', { count: more }),
              },
            ]
          : []),
      ];

  return (
    <AutoComplete
      id={id}
      style={style}
      value={value ?? ''}
      options={options}
      filterOption={false}
      // Nothing found is said by saying nothing: the box takes the text either way, and an empty
      // panel under it would read as a refusal.
      notFoundContent={null}
      onChange={(text) => onChange?.(String(text ?? ''))}
      {...aria}
    >
      {/* Drawn as a plain text box rather than left to the suggestion control's own, so the text
          on screen is the text the form reads. As long as a station's name can be. */}
      <Input data-testid={testId} maxLength={400} autoComplete="off" />
    </AutoComplete>
  );
}
