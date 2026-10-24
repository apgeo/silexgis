// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Card, Checkbox, Flex, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSaveAccountPreferences } from '../../api/hooks/accountPrefs.ts';
import { choiceLabel, formatLabel, settingLabel } from '../../components/coordinates/formatLabels.ts';
import { formatCoordinate, type CoordinateFormat } from '../../geo/coordinateFormat.ts';
import { FORMAT_IDS, FORMATS, writePosition, type FormatId, type FormatSettings } from '../../geo/formats/registry.ts';
import { READING_RULE_CHOICES, type ReadingRules } from '../../geo/formats/settings.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/**
 * The point each format is previewed on, so a setting is chosen by what it does to a position:
 * the summit of Moldoveanu, a public trig point, rather than anything from the registry — except
 * for the Swiss grids, which have no reference for Romania and are previewed on their own origin,
 * the former observatory of Bern.
 */
const SAMPLE_POINT: [number, number] = [24.73617, 45.59944];
const SWISS_SAMPLE_POINT: [number, number] = [7.43863, 46.95108];

function samplePointFor(id: FormatId): [number, number] {
  return id === 'lv95' || id === 'lv03' ? SWISS_SAMPLE_POINT : SAMPLE_POINT;
}

const NOTES: Partial<Record<FormatId, true>> = { utm: true, mgrs: true, stereo70: true, lv95: true, lv03: true, pluscode: true };

/**
 * Settings › Coordinates: the format positions are written in, how a typed or pasted position is
 * read where its text leaves that open, the other formats a position shown in detail is also listed
 * in, and each format's own settings with a live preview.
 *
 * Every control writes the local copy at once and saves to the account in the background, the way
 * the other settings pages do, so a preview follows the choice without a round trip.
 */
export default function CoordinateSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const coordinateFormat = useUiPrefsStore((s) => s.coordinateFormat);
  const setCoordinateFormat = useUiPrefsStore((s) => s.setCoordinateFormat);
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  const setFormatSettings = useUiPrefsStore((s) => s.setFormatSettings);
  const listed = useUiPrefsStore((s) => s.coordinateFormatsListed);
  const setListed = useUiPrefsStore((s) => s.setCoordinateFormatsListed);
  const reading = useUiPrefsStore((s) => s.coordinateReading);
  const setReading = useUiPrefsStore((s) => s.setCoordinateReading);

  const save = useSaveAccountPreferences();
  const saveNow = () => save.mutate(undefined, { onError: () => message.error(t('common.saveFailed')) });

  const preview = (id: FormatId) => {
    const [lon, lat] = samplePointFor(id);
    return writePosition(id, lon, lat, settings) ?? t('coordinates.settings.notAvailable');
  };

  return (
    <Flex vertical gap={16}>
      <Card title={t('coordinates.settings.title')}>
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('coordinates.settings.intro')}</Typography.Text>
          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('coordinates.settings.shownAs')}</Typography.Text>
            <Select
              style={{ minWidth: 280, maxWidth: '100%' }}
              popupMatchSelectWidth={false}
              value={coordinateFormat}
              onChange={(format: CoordinateFormat) => {
                setCoordinateFormat(format);
                saveNow();
              }}
              aria-label={t('coordinates.settings.shownAs')}
              data-testid="coordinate-format"
              options={FORMAT_IDS.map((value) => ({
                value,
                label: `${formatLabel(t, value)} — ${formatCoordinate(SAMPLE_POINT[0], SAMPLE_POINT[1], value, settings)}`,
              }))}
            />
          </Flex>
          <Typography.Text type="secondary">{t('coordinates.settings.sample')}</Typography.Text>
        </Flex>
      </Card>

      <Card title={t('coordinates.settings.reading.title')}>
        <Flex vertical gap={12}>
          <Typography.Text type="secondary">{t('coordinates.settings.reading.intro')}</Typography.Text>
          {(Object.keys(READING_RULE_CHOICES) as (keyof ReadingRules)[]).map((rule) => (
            <Flex key={rule} gap={12} align="center" wrap>
              <Typography.Text style={{ minWidth: 160 }}>{t(`coordinates.settings.reading.${rule}`)}</Typography.Text>
              <Select
                style={{ minWidth: 280, maxWidth: '100%' }}
                popupMatchSelectWidth={false}
                value={reading[rule]}
                onChange={(value: string) => {
                  setReading({ [rule]: value } as Partial<ReadingRules>);
                  saveNow();
                }}
                aria-label={t(`coordinates.settings.reading.${rule}`)}
                data-testid={`coordinate-reading-${rule}`}
                options={READING_RULE_CHOICES[rule].map((value) => ({
                  value,
                  label: t(`coordinates.settings.reading.${rule}Values.${value}`),
                }))}
              />
            </Flex>
          ))}
        </Flex>
      </Card>

      <Card title={t('coordinates.settings.listed')}>
        <Flex vertical gap={12}>
          <Typography.Text type="secondary">{t('coordinates.settings.listedHint')}</Typography.Text>
          <Checkbox.Group
            value={listed}
            onChange={(values) => {
              // Kept in the registry's order whatever order they were ticked in.
              const ticked = new Set(values as FormatId[]);
              setListed(FORMAT_IDS.filter((id) => ticked.has(id)));
              saveNow();
            }}
            aria-label={t('coordinates.settings.listed')}
          >
            <Flex vertical gap={4}>
              {FORMAT_IDS.map((id) => (
                <Checkbox key={id} value={id}>
                  {formatLabel(t, id)}
                </Checkbox>
              ))}
            </Flex>
          </Checkbox.Group>
        </Flex>
      </Card>

      <Card title={t('coordinates.settings.perFormat')}>
        <Flex vertical gap={20}>
          {FORMAT_IDS.map((id) => (
            <FormatSettingsRow
              key={id}
              id={id}
              settings={settings}
              preview={preview(id)}
              onChange={(next) => {
                setFormatSettings(id, next as FormatSettings[typeof id]);
                saveNow();
              }}
            />
          ))}
        </Flex>
      </Card>
    </Flex>
  );
}

function FormatSettingsRow({
  id,
  settings,
  preview,
  onChange,
}: {
  id: FormatId;
  settings: FormatSettings;
  preview: string;
  onChange: (next: Record<string, string | number>) => void;
}) {
  const { t } = useTranslation();
  const own = settings[id] as Record<string, string | number>;
  const choices = FORMATS[id].choices as Record<string, readonly (string | number)[]>;
  return (
    <Flex vertical gap={8} data-testid={`coordinate-settings-${id}`}>
      <Typography.Text strong>{formatLabel(t, id)}</Typography.Text>
      <Flex gap={12} wrap align="center">
        {Object.keys(choices).map((key) => (
          <Flex key={key} gap={8} align="center">
            <Typography.Text type="secondary">{settingLabel(t, key)}</Typography.Text>
            <Select
              style={{ minWidth: 120 }}
              popupMatchSelectWidth={false}
              value={own[key]}
              aria-label={`${formatLabel(t, id)} — ${settingLabel(t, key)}`}
              onChange={(value: string | number) => onChange({ ...own, [key]: value })}
              options={choices[key].map((value) => ({ value, label: choiceLabel(t, key, value) }))}
            />
          </Flex>
        ))}
      </Flex>
      <Typography.Text code data-testid={`coordinate-preview-${id}`}>
        {preview}
      </Typography.Text>
      {NOTES[id] && <Typography.Text type="secondary">{t(`coordinates.settings.notes.${id}`)}</Typography.Text>}
    </Flex>
  );
}
