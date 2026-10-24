// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Input, Select, Space, Typography } from 'antd';
import { useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { FORMAT_IDS, isFormatId, readPosition, writePosition, type FormatId } from '../../geo/formats/registry.ts';
import type { ParsedPosition } from '../../geo/formats/types.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import Button from '../Button.tsx';
import { assumptionLabel, formatLabel, parseErrorLabel } from './formatLabels.ts';

type FormatChoice = 'any' | FormatId;

/** A reading shown back in decimal degrees to six places, the form nobody can misread. */
function readBack(position: ParsedPosition): string {
  return writePosition('dd', position.lon, position.lat, { dd: { decimals: 6, style: 'letters' } }) ?? '';
}

/**
 * A position typed or pasted in any format this application reads — decimal degrees, minutes,
 * seconds, UTM, MGRS, a national grid, a Plus Code — and handed on as WGS84 longitude and latitude,
 * which is what every position is stored as. What the text was read as is said back before it is
 * used, with anything that had to be assumed; a text that can be read more than one way offers each
 * reading, and the format can be named outright when the guess is wrong.
 *
 * The field never shows the position it is beside: it is for entering one, so a position the
 * reader may only see approximately is never echoed into it.
 */
export default function PositionTextInput({
  onPosition,
  disabled = false,
}: {
  onPosition: (lonLat: [number, number]) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState('');
  const [choice, setChoice] = useState<FormatChoice>('any');
  const feedbackId = useId();
  const rules = useUiPrefsStore((s) => s.coordinateReading);

  const reading = useMemo(
    () => (text.trim() === '' ? null : readPosition(text, choice === 'any' ? undefined : choice, rules)),
    [text, choice, rules],
  );
  const candidates = reading?.kind === 'parsed' ? reading.candidates : [];
  const use = (position: ParsedPosition) => onPosition([position.lon, position.lat]);
  const nameOf = (format: string) => (isFormatId(format) ? formatLabel(t, format) : format);

  return (
    <Flex vertical gap={4} style={{ width: '100%' }}>
      <Flex gap={8} wrap justify="space-between">
        <Typography.Text>{t('coordinates.input.label')}</Typography.Text>
        {/* In a new tab, so that whatever is being edited here is kept while a position is worked out. */}
        <Link to="/tools/coordinates" target="_blank" rel="noopener">
          {t('coordinates.input.openConverter')}
        </Link>
      </Flex>
      <Space.Compact style={{ width: '100%' }}>
        <Select<FormatChoice>
          value={choice}
          onChange={setChoice}
          disabled={disabled}
          popupMatchSelectWidth={false}
          style={{ width: 170 }}
          aria-label={t('coordinates.input.anyFormat')}
          options={[
            { value: 'any', label: t('coordinates.input.anyFormat') },
            ...FORMAT_IDS.map((id) => ({ value: id, label: formatLabel(t, id) })),
          ]}
        />
        <Input
          value={disabled ? '' : text}
          disabled={disabled}
          onChange={(event) => setText(event.target.value)}
          onPressEnter={(event) => {
            if (candidates.length === 1) {
              event.preventDefault();
              use(candidates[0]);
            }
          }}
          placeholder={t('coordinates.input.placeholder')}
          aria-label={t('coordinates.input.label')}
          aria-describedby={feedbackId}
          aria-invalid={reading?.kind === 'invalid'}
          data-testid="position-text-input"
        />
        <Button disabled={disabled || candidates.length !== 1} onClick={() => use(candidates[0])}>
          {t('coordinates.input.use')}
        </Button>
      </Space.Compact>
      <div id={feedbackId} aria-live="polite" data-testid="position-text-feedback">
        {!disabled && reading?.kind === 'invalid' && (
          <Typography.Text type="danger">{parseErrorLabel(t, reading.error)}</Typography.Text>
        )}
        {!disabled && candidates.length === 1 && (
          <Flex vertical>
            <Typography.Text type="secondary">
              {t('coordinates.input.readAs', { format: nameOf(candidates[0].format), position: readBack(candidates[0]) })}
            </Typography.Text>
            {candidates[0].assumptions.map((assumption) => (
              <Typography.Text key={assumption} type="secondary">
                {assumptionLabel(t, assumption)}
              </Typography.Text>
            ))}
          </Flex>
        )}
        {!disabled && candidates.length > 1 && (
          <Flex vertical gap={2}>
            <Typography.Text type="secondary">{t('coordinates.input.pick')}</Typography.Text>
            {candidates.map((candidate, index) => (
              <Flex key={index} gap={8} align="center" wrap>
                <Button size="small" onClick={() => use(candidate)}>
                  {t('coordinates.input.use')}
                </Button>
                <Typography.Text>
                  {t('coordinates.input.readAs', { format: nameOf(candidate.format), position: readBack(candidate) })}
                </Typography.Text>
                {candidate.assumptions.map((assumption) => (
                  <Typography.Text key={assumption} type="secondary">
                    {assumptionLabel(t, assumption)}
                  </Typography.Text>
                ))}
              </Flex>
            ))}
          </Flex>
        )}
      </div>
    </Flex>
  );
}
