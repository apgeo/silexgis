// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { BorderOutlined, ColumnHeightOutlined, PictureOutlined } from '@ant-design/icons';
import type { MapViewKind } from './vocabulary.ts';

/**
 * The glyph each map view kind wears on its tab, everywhere maps are tabs.
 *
 * One home because two surfaces now build the strip — the survey viewer modal and the
 * coordinator's tracking panel — and a plan sheet that changed its icon between them
 * would read as a different map of the same cave.
 */
export const VIEW_KIND_ICONS: Record<MapViewKind, ReactNode> = {
  plan: <BorderOutlined />,
  profile: <ColumnHeightOutlined />,
  other: <PictureOutlined />,
};
