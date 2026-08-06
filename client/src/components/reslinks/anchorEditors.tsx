// SPDX-License-Identifier: AGPL-3.0-or-later
import { InputNumber, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { formatMediaTime, readAnchorNumber, type AnchorEditorProps } from './anchorTypes.ts';

/**
 * Editors for the anchor kinds that need nothing but numbers: a page, a span of pages, a
 * moment in a recording, a span of one. The richer selectors — text ranges, image regions,
 * survey stations, waypoints — each ride the viewer that can express them and arrive as
 * registry entries later; these four need no viewer at all, so they ship now.
 *
 * Each editor is a plain value/onChange field, which is what a form drives it as. What each
 * one will and will not accept lives beside the other anchor rules, so the registry can ask
 * the same question of a payload whether or not it was composed here.
 */

/** Patches one field of an anchor object, dropping the key entirely when it is cleared. */
function patch(anchor: unknown, key: string, value: number | null): Record<string, unknown> {
  const base: Record<string, unknown> =
    typeof anchor === 'object' && anchor !== null ? { ...(anchor as Record<string, unknown>) } : {};
  if (value === null) {
    delete base[key];
  } else {
    base[key] = value;
  }
  return base;
}

export function PageAnchorEditor({ value, onChange }: AnchorEditorProps) {
  const { t } = useTranslation();
  return (
    <InputNumber
      min={1}
      step={1}
      precision={0}
      placeholder={t('resLinks.anchorEditors.page')}
      aria-label={t('resLinks.anchorEditors.page')}
      value={readAnchorNumber(value, 'page')}
      onChange={(next) => onChange(patch(value, 'page', next))}
    />
  );
}

export function PageRangeAnchorEditor({ value, onChange }: AnchorEditorProps) {
  const { t } = useTranslation();
  return (
    <Space>
      <InputNumber
        min={1}
        step={1}
        precision={0}
        placeholder={t('resLinks.anchorEditors.fromPage')}
        aria-label={t('resLinks.anchorEditors.fromPage')}
        value={readAnchorNumber(value, 'fromPage')}
        onChange={(next) => onChange(patch(value, 'fromPage', next))}
      />
      <Typography.Text type="secondary">–</Typography.Text>
      <InputNumber
        min={1}
        step={1}
        precision={0}
        placeholder={t('resLinks.anchorEditors.toPage')}
        aria-label={t('resLinks.anchorEditors.toPage')}
        value={readAnchorNumber(value, 'toPage')}
        onChange={(next) => onChange(patch(value, 'toPage', next))}
      />
    </Space>
  );
}

/** Seconds in, `m:ss` echoed back — the number is the contract, the echo is the reassurance. */
function SecondsInput({
  seconds,
  label,
  onChangeSeconds,
}: {
  seconds: number | null;
  label: string;
  onChangeSeconds: (value: number | null) => void;
}) {
  return (
    <Space size={4}>
      <InputNumber
        min={0}
        step={1}
        placeholder={label}
        aria-label={label}
        value={seconds}
        onChange={onChangeSeconds}
      />
      <Typography.Text type="secondary">
        {seconds === null ? '' : formatMediaTime(seconds)}
      </Typography.Text>
    </Space>
  );
}

export function TimePointAnchorEditor({ value, onChange }: AnchorEditorProps) {
  const { t } = useTranslation();
  return (
    <SecondsInput
      seconds={readAnchorNumber(value, 't')}
      label={t('resLinks.anchorEditors.seconds')}
      onChangeSeconds={(next) => onChange(patch(value, 't', next))}
    />
  );
}

export function TimeRangeAnchorEditor({ value, onChange }: AnchorEditorProps) {
  const { t } = useTranslation();
  return (
    <Space>
      <SecondsInput
        seconds={readAnchorNumber(value, 'start')}
        label={t('resLinks.anchorEditors.start')}
        onChangeSeconds={(next) => onChange(patch(value, 'start', next))}
      />
      <Typography.Text type="secondary">–</Typography.Text>
      <SecondsInput
        seconds={readAnchorNumber(value, 'end')}
        label={t('resLinks.anchorEditors.end')}
        onChangeSeconds={(next) => onChange(patch(value, 'end', next))}
      />
    </Space>
  );
}
