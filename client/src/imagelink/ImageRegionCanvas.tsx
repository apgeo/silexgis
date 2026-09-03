// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  IMAGE_REGION_SHAPES,
  POINT_RADIUS_PX,
  clampFraction,
  hitTest,
  pixelBounds,
  polygonFrom,
  regionFromDrag,
  toPixels,
  type DrawnSize,
  type ImageRegion,
  type ImageRegionShape,
  type PixelRegion,
} from './regions.ts';

/**
 * A picture with regions drawn over it — the one surface that both authors a region and shows the
 * ones already stored.
 *
 * <b>It is one component rather than two on purpose.</b> Drawing a region and reading one are the
 * same geometry seen from either end, and the two would otherwise have to agree, in two places,
 * about how a fraction becomes a pixel — which is exactly the agreement that quietly stops holding
 * and puts an author's shape somewhere a reader does not find it.
 *
 * <b>What it measures, and why it re-measures.</b> Regions are fractions of the picture, so
 * everything on screen depends on the size the picture is actually being drawn at. That is not
 * known until the bytes arrive, and it changes when the window does, when a panel is dragged, and
 * when the picture is swapped for another. So the box is observed rather than read once: a size
 * captured at load and trusted afterwards is right until the first time somebody resizes anything,
 * and then every shape is drawn in the wrong place with nothing to say so.
 */

export interface DrawnRegion {
  /** Identifies the region to the caller — a link member id in the reader, absent in the editor. */
  id: string;
  region: ImageRegion;
  /** Outline colour. */
  stroke: string;
  /** Wash inside the outline. */
  fill: string;
  /** What this region is, said in words for a reader who cannot see the colours. */
  label?: string;
}

export interface ImageRegionCanvasProps {
  src: string;
  alt: string;
  regions: DrawnRegion[];
  /** Drawn heavier than the rest: the one the reader was sent here to look at. */
  highlightedId?: string | null;
  /** Which shape a new region would be, or null when this surface only shows them. */
  drawing?: ImageRegionShape | null;
  /** A finished region. The caller decides whether that replaces the draft or adds to a set. */
  onDrawn?: (region: ImageRegion) => void;
  /** A stored region was clicked. Never called while drawing — a drag must not select. */
  onPick?: (id: string) => void;
  /** Bounds the drawn picture so a tall photograph does not push everything else off screen. */
  maxHeight?: number;
}

/** The SVG geometry for one region, at the size the picture is drawn. */
function shapeOf(region: PixelRegion, key: string, stroke: string, fill: string, chosen: boolean) {
  const common = {
    stroke,
    fill,
    // The one the reader was sent to is drawn heavier rather than in a different colour: colour
    // already means which relation this is, and a second meaning on the same channel is a legend
    // nobody can read.
    strokeWidth: chosen ? 4 : 2,
    vectorEffect: 'non-scaling-stroke' as const,
    strokeLinejoin: 'round' as const,
  };
  switch (region.shape) {
    case 'point':
      // A point has no extent to fill, so it is drawn as a ring of a fixed size on screen: the
      // thing it marks is a place, and a place does not get bigger because the picture did.
      return <circle key={key} cx={region.x} cy={region.y} r={POINT_RADIUS_PX} {...common} />;
    case 'rect':
      return (
        <rect key={key} x={region.x} y={region.y} width={region.w} height={region.h} {...common} />
      );
    case 'circle':
      return <circle key={key} cx={region.cx} cy={region.cy} r={region.r} {...common} />;
    case 'polygon':
      return (
        <polygon
          key={key}
          points={region.points.map(([x, y]) => `${x},${y}`).join(' ')}
          {...common}
        />
      );
  }
}

export default function ImageRegionCanvas({
  src,
  alt,
  regions,
  highlightedId = null,
  drawing = null,
  onDrawn,
  onPick,
  maxHeight,
}: ImageRegionCanvasProps) {
  const imageRef = useRef<HTMLImageElement | null>(null);
  const [size, setSize] = useState<DrawnSize | null>(null);
  const [drag, setDrag] = useState<{ from: { x: number; y: number }; to: { x: number; y: number } } | null>(null);
  const [vertices, setVertices] = useState<{ x: number; y: number }[]>([]);

  const measure = useCallback(() => {
    const element = imageRef.current;
    if (element === null) {
      return;
    }
    const box = element.getBoundingClientRect();
    // A picture that has not arrived, or a panel with no room in it, measures zero — and dividing
    // by that would put every region at infinity. Treated as "not measured yet" instead.
    setSize(box.width > 0 && box.height > 0 ? { width: box.width, height: box.height } : null);
  }, []);

  useEffect(() => {
    const element = imageRef.current;
    if (element === null || typeof ResizeObserver === 'undefined') {
      // Without an observer the load handler is still wired, so the picture is drawn correctly
      // until something resizes. Better than nothing, and the environments without one are test
      // environments rather than browsers.
      measure();
      return;
    }

    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, [measure]);

  // A different picture is a different size, and the shapes over it are somebody else's.
  useEffect(() => {
    setDrag(null);
    setVertices([]);
  }, [src, drawing]);

  const positionOf = useCallback(
    (event: { clientX: number; clientY: number }): { x: number; y: number } | null => {
      const element = imageRef.current;
      if (element === null) {
        return null;
      }
      const box = element.getBoundingClientRect();
      if (box.width <= 0 || box.height <= 0) {
        return null;
      }
      return {
        x: clampFraction((event.clientX - box.left) / box.width),
        y: clampFraction((event.clientY - box.top) / box.height),
      };
    },
    [],
  );

  const finishPolygon = useCallback(() => {
    const region = polygonFrom(vertices);
    if (region !== null) {
      onDrawn?.(region);
    }
    setVertices([]);
  }, [onDrawn, vertices]);

  const onPointerDown = (event: React.PointerEvent) => {
    if (drawing === null) {
      return;
    }
    const at = positionOf(event);
    if (at === null) {
      return;
    }

    if (drawing === 'point') {
      onDrawn?.({ shape: 'point', x: at.x, y: at.y });
      return;
    }

    if (drawing === 'polygon') {
      setVertices((current) => [...current, at]);
      return;
    }

    // The pointer is captured so that a drag which leaves the picture still ends here rather than
    // being lost to whatever it passed over — the coordinates are clamped, so a drag off the edge
    // means "to the edge" instead of meaning nothing.
    (event.target as Element).setPointerCapture?.(event.pointerId);
    setDrag({ from: at, to: at });
  };

  const onPointerMove = (event: React.PointerEvent) => {
    if (drag === null) {
      return;
    }
    const at = positionOf(event);
    if (at !== null) {
      setDrag({ from: drag.from, to: at });
    }
  };

  const onPointerUp = () => {
    if (drag === null || size === null || (drawing !== 'rect' && drawing !== 'circle')) {
      setDrag(null);
      return;
    }
    const region = regionFromDrag(drawing, drag.from, drag.to, size);
    setDrag(null);
    if (region !== null) {
      onDrawn?.(region);
    }
  };

  const onClickSurface = (event: React.MouseEvent) => {
    // Picking is off while drawing: a press that starts a rectangle inside an existing region
    // would otherwise both draw and select, and the reader gets whichever the browser reports last.
    if (drawing !== null || onPick === undefined || size === null) {
      return;
    }
    const element = imageRef.current;
    if (element === null) {
      return;
    }
    const box = element.getBoundingClientRect();
    const x = event.clientX - box.left;
    const y = event.clientY - box.top;

    // Smallest first, so a region drawn inside another is reachable at all. Without it the
    // enclosing shape answers every click in its own area and the inner one can never be picked.
    const candidates = regions
      .map((entry) => ({ entry, pixels: toPixels(entry.region, size) }))
      .filter(({ pixels }) => hitTest(pixels, x, y))
      .sort((a, b) => {
        const one = pixelBounds(a.pixels);
        const other = pixelBounds(b.pixels);
        return one.w * one.h - other.w * other.h;
      });

    if (candidates.length > 0) {
      onPick(candidates[0].entry.id);
    }
  };

  const draft = drag !== null && size !== null && (drawing === 'rect' || drawing === 'circle')
    ? regionFromDrag(drawing, drag.from, drag.to, size)
    : null;

  return (
    <div style={{ position: 'relative', display: 'inline-block', maxWidth: '100%', lineHeight: 0 }}>
      <img
        ref={imageRef}
        src={src}
        alt={alt}
        onLoad={measure}
        draggable={false}
        style={{
          display: 'block',
          maxWidth: '100%',
          maxHeight,
          height: 'auto',
          userSelect: 'none',
          touchAction: drawing === null ? undefined : 'none',
        }}
      />
      {size !== null && (
        <svg
          width={size.width}
          height={size.height}
          viewBox={`0 0 ${size.width} ${size.height}`}
          style={{
            position: 'absolute',
            inset: 0,
            // The overlay must not eat clicks meant for the picture unless it is doing something
            // with them, and the cursor has to say which of the two states this is.
            pointerEvents: drawing !== null || onPick !== undefined ? 'auto' : 'none',
            cursor: drawing !== null ? 'crosshair' : onPick !== undefined ? 'pointer' : 'default',
          }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={() => setDrag(null)}
          onClick={onClickSurface}
          onDoubleClick={drawing === 'polygon' ? finishPolygon : undefined}
        >
          {regions.map((entry) => {
            const pixels = toPixels(entry.region, size);
            const chosen = entry.id === highlightedId;
            return (
              <g
                key={entry.id}
                opacity={highlightedId === null || chosen ? 1 : 0.55}
                aria-label={entry.label}
              >
                {shapeOf(pixels, entry.id, entry.stroke, entry.fill, chosen)}
              </g>
            );
          })}
          {draft !== null
            && shapeOf(toPixels(draft, size), 'draft', '#1677ff', 'rgba(22, 119, 255, 0.2)', false)}
          {vertices.length > 0 && (
            <g>
              <polyline
                points={vertices.map((v) => `${v.x * size.width},${v.y * size.height}`).join(' ')}
                fill="none"
                stroke="#1677ff"
                strokeWidth={2}
                strokeDasharray="4 3"
              />
              {vertices.map((v, index) => (
                <circle
                  key={index}
                  cx={v.x * size.width}
                  cy={v.y * size.height}
                  r={4}
                  fill="#1677ff"
                />
              ))}
            </g>
          )}
        </svg>
      )}
    </div>
  );
}

/** Re-exported so a caller offering the shapes does not also have to import the model. */
export { IMAGE_REGION_SHAPES };
export type { ImageRegionShape };
