// SPDX-License-Identifier: AGPL-3.0-or-later
import { DownOutlined, RightOutlined } from '@ant-design/icons';
import { Flex, Typography } from 'antd';
import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { DEFAULT_PROTECTION_GRID_METRES } from '../../geo/formats/protection.ts';
import { writePosition } from '../../geo/formats/registry.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import Button from '../Button.tsx';
import { formatLabel } from './formatLabels.ts';

/**
 * "Other formats": a position shown in detail, listed in every format the person chose to list it
 * in besides the one it is shown in, each with a copy button — for the member who reads decimal
 * degrees but has to hand a position to a rescue team in UTM, or type it into a GPS that wants
 * minutes.
 *
 * A format with no reference for the position (a national grid outside its country, UTM near a
 * pole) is left out rather than listed as unavailable. A protected position is written no finer
 * than the protection grid in every format, and copied exactly as it is written.
 */
export default function OtherFormats({ lon, lat, approximate = false }: { lon: number; lat: number; approximate?: boolean }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const listId = useId();
  const shown = useUiPrefsStore((s) => s.coordinateFormat);
  const settings = useUiPrefsStore((s) => s.coordinateSettings);
  const listed = useUiPrefsStore((s) => s.coordinateFormatsListed);

  const rows = open
    ? listed
        .filter((id) => id !== shown)
        .map((id) => ({
          id,
          text: writePosition(
            id,
            lon,
            lat,
            settings,
            approximate ? { protectedGridMetres: DEFAULT_PROTECTION_GRID_METRES } : {},
          ),
        }))
        .filter((row): row is { id: typeof row.id; text: string } => row.text !== null)
    : [];

  if (listed.every((id) => id === shown)) {
    return null;
  }

  return (
    <Flex vertical gap={4}>
      <Button
        type="link"
        size="small"
        style={{ padding: 0, alignSelf: 'flex-start' }}
        icon={open ? <DownOutlined /> : <RightOutlined />}
        aria-expanded={open}
        aria-controls={listId}
        onClick={() => setOpen((value) => !value)}
      >
        {open ? t('coordinates.others.hide') : t('coordinates.others.show')}
      </Button>
      {open && (
        <Flex vertical gap={2} id={listId} data-testid="other-formats">
          {approximate && <Typography.Text type="secondary">{t('coordinates.others.approximate')}</Typography.Text>}
          {rows.map(({ id, text }) => (
            <Flex key={id} gap={8} wrap>
              <Typography.Text type="secondary">{formatLabel(t, id)}</Typography.Text>
              <Typography.Text copyable={{ text }} data-testid={`other-format-${id}`}>
                {text}
              </Typography.Text>
            </Flex>
          ))}
          {/* Handed to the converter only when exact: a protected position written into it would be
              converted, and copied, finer than it may be shown. */}
          {!approximate && (
            <Link
              to={`/tools/coordinates?q=${encodeURIComponent(`${lat.toFixed(7)}, ${lon.toFixed(7)}`)}`}
              data-testid="other-formats-converter"
            >
              {t('coordinates.others.openConverter')}
            </Link>
          )}
        </Flex>
      )}
    </Flex>
  );
}
