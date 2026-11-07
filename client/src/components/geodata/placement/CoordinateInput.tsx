// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { InputNumber } from 'antd';

interface Props {
  value: number | null;
  /** Called once a typed value is finished — on leaving the field or pressing Enter. */
  onCommit: (value: number) => void;
  min: number;
  max: number;
  /** Decimal places: six for degrees, two for metres of a grid. */
  precision?: number;
  disabled?: boolean;
  'aria-label': string;
  'data-testid'?: string;
}

/**
 * A coordinate typed in — degrees, or metres of a grid — handed on only when the typing is done.
 *
 * Every keystroke of "24.0031" is a number on its way to the one meant, and each of them would
 * move a point across the country and ask the server for a preview of a shape nobody wants.
 * So what is being typed stays here until the field is left or Enter is pressed.
 */
export default function CoordinateInput({ value, onCommit, min, max, precision = 6, disabled, ...rest }: Props) {
  const [draft, setDraft] = useState<number | null | undefined>(undefined);

  const commit = () => {
    if (typeof draft === 'number' && Number.isFinite(draft) && draft !== value) {
      onCommit(draft);
    }
    setDraft(undefined);
  };

  return (
    <InputNumber<number>
      size="small"
      min={min}
      max={max}
      step={10 ** -precision}
      precision={precision}
      disabled={disabled}
      style={{ width: 140 }}
      value={draft === undefined ? value : draft}
      onChange={(next) => setDraft(next)}
      onBlur={commit}
      onPressEnter={commit}
      aria-label={rest['aria-label']}
      data-testid={rest['data-testid']}
    />
  );
}
