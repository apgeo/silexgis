// SPDX-License-Identifier: AGPL-3.0-or-later
import { metersBetween, type Camera3DEye } from '../scene3d/camera3d.ts';

// Lines a camera can follow: down a cave's survey from its highest point to its deepest, and along
// the track of an imported file.
//
// Neither is ever written into a flyby. A flyby says "down this cave" or "along this file", and the
// line is found when the flyby is prepared, from what the scene draws for the person playing it —
// so a flyby of a cave says nothing of its survey, and follows the survey as it is on the day it is
// played.

/** A point of a line as the scene draws it: degrees, and metres above the ellipsoid. */
export interface FlybyLinePoint {
  longitude: number;
  latitude: number;
  height: number;
}

/** Positions closer than this are one survey station, whatever line they arrived in. */
const STATION_DIGITS = 7;
const HEIGHT_DIGITS = 2;

function stationKey(point: FlybyLinePoint): string {
  return `${point.longitude.toFixed(STATION_DIGITS)},${point.latitude.toFixed(STATION_DIGITS)},${point.height.toFixed(HEIGHT_DIGITS)}`;
}

function eyeOf(point: FlybyLinePoint): Camera3DEye {
  return { lon: point.longitude, lat: point.latitude, height: point.height };
}

/** A small binary heap of [distance, node], nearest first: enough for a survey of tens of thousands of stations. */
class Queue {
  private items: [number, number][] = [];

  get size(): number {
    return this.items.length;
  }

  push(item: [number, number]): void {
    const items = this.items;
    items.push(item);
    let at = items.length - 1;
    while (at > 0) {
      const parent = (at - 1) >> 1;
      if (items[parent][0] <= items[at][0]) {
        break;
      }
      [items[parent], items[at]] = [items[at], items[parent]];
      at = parent;
    }
  }

  pop(): [number, number] {
    const items = this.items;
    const top = items[0];
    const last = items.pop()!;
    if (items.length > 0) {
      items[0] = last;
      let at = 0;
      for (;;) {
        const left = at * 2 + 1;
        const right = left + 1;
        let smallest = at;
        if (left < items.length && items[left][0] < items[smallest][0]) {
          smallest = left;
        }
        if (right < items.length && items[right][0] < items[smallest][0]) {
          smallest = right;
        }
        if (smallest === at) {
          break;
        }
        [items[smallest], items[at]] = [items[at], items[smallest]];
        at = smallest;
      }
    }
    return top;
  }
}

/**
 * The way down a cave's survey: from its highest drawn point to its deepest, through the legs that
 * join them, the shortest such way when there are several. Undefined when there is no such way —
 * the survey is not drawn, it carries no heights, or the highest and the deepest point are on lines
 * that do not meet (the scene drew only part of the cave, or the survey has parts never tied in).
 *
 * The lines are as the scene draws them, so the depths are the drawn depths, and a station shared by
 * two lines — a junction — is one point here because both lines give it the same position.
 */
export function surveyRoute(lines: readonly (readonly FlybyLinePoint[])[]): Camera3DEye[] | undefined {
  const index = new Map<string, number>();
  const points: FlybyLinePoint[] = [];
  const neighbours: [number, number][][] = [];
  const node = (point: FlybyLinePoint) => {
    const key = stationKey(point);
    let at = index.get(key);
    if (at === undefined) {
      at = points.length;
      index.set(key, at);
      points.push(point);
      neighbours.push([]);
    }
    return at;
  };
  for (const line of lines) {
    for (let i = 1; i < line.length; i += 1) {
      const a = node(line[i - 1]);
      const b = node(line[i]);
      if (a !== b) {
        const length = metersBetween(eyeOf(line[i - 1]), eyeOf(line[i]));
        neighbours[a].push([b, length]);
        neighbours[b].push([a, length]);
      }
    }
  }
  if (points.length < 2) {
    return undefined;
  }
  let top = 0;
  let bottom = 0;
  points.forEach((point, at) => {
    if (point.height > points[top].height) {
      top = at;
    }
    if (point.height < points[bottom].height) {
      bottom = at;
    }
  });
  if (top === bottom || !(points[top].height > points[bottom].height)) {
    return undefined;
  }

  const distance = new Float64Array(points.length).fill(Number.POSITIVE_INFINITY);
  const previous = new Int32Array(points.length).fill(-1);
  distance[top] = 0;
  const queue = new Queue();
  queue.push([0, top]);
  while (queue.size > 0) {
    const [reached, at] = queue.pop();
    if (reached > distance[at]) {
      continue;
    }
    if (at === bottom) {
      break;
    }
    for (const [next, length] of neighbours[at]) {
      const through = reached + length;
      if (through < distance[next]) {
        distance[next] = through;
        previous[next] = at;
        queue.push([through, next]);
      }
    }
  }
  if (!Number.isFinite(distance[bottom])) {
    return undefined;
  }
  const route: Camera3DEye[] = [];
  for (let at = bottom; at !== -1; at = previous[at]) {
    route.push(eyeOf(points[at]));
  }
  return route.reverse();
}

/**
 * The line a camera follows along an imported file: its longest stretch of line, laid on the ground
 * the scene draws. Undefined when the file holds no line at all — only waypoints, say.
 */
export function trackRoute(
  lines: readonly (readonly { longitude: number; latitude: number }[])[],
  groundHeight: (longitude: number, latitude: number) => number,
): Camera3DEye[] | undefined {
  let longest: readonly { longitude: number; latitude: number }[] | undefined;
  let longestLength = 0;
  for (const line of lines) {
    let length = 0;
    for (let i = 1; i < line.length; i += 1) {
      length += metersBetween(
        { lon: line[i - 1].longitude, lat: line[i - 1].latitude, height: 0 },
        { lon: line[i].longitude, lat: line[i].latitude, height: 0 },
      );
    }
    if (length > longestLength) {
      longest = line;
      longestLength = length;
    }
  }
  if (!longest || !(longestLength > 0)) {
    return undefined;
  }
  return longest.map((point) => ({
    lon: point.longitude,
    lat: point.latitude,
    height: groundHeight(point.longitude, point.latitude),
  }));
}
