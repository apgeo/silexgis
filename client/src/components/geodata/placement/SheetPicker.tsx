// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import type { PointPair } from '../../../geodata/placement/placement.ts';
import RasterMapView from '../../../rastermap/RasterMapView.tsx';

interface Props {
  /** The drawn page as a picture. */
  imageUrl: string;
  alt: string;
  points: readonly PointPair[];
  /** Answers a click on the sheet with the point in picture fractions; absent while not picking. */
  onPick?: (u: number, v: number) => void;
  active: boolean;
}

/**
 * The sheet itself, where the picture half of each control point is chosen. Its fractions are
 * measured from the top-left corner, which is the frame a placement's points are kept in.
 */
export default function SheetPicker({ imageUrl, alt, points, onPick, active }: Props) {
  const markers = useMemo(
    () => points.map((point, index) => ({ key: String(index), label: String(index + 1), x: point.u, y: point.v })),
    [points],
  );
  return (
    <RasterMapView
      imageUrl={imageUrl}
      alt={alt}
      markers={markers}
      active={active}
      height={420}
      testId="placement-sheet"
      onMapClick={onPick ? (at) => onPick(at.x, at.y) : undefined}
    />
  );
}
