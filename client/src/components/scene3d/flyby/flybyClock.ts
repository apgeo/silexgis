// SPDX-License-Identifier: AGPL-3.0-or-later

/** A length of a flyby as a person reads it: minutes and seconds, `1:05`. */
export function flybyClock(seconds: number): string {
  const whole = Math.max(0, Math.round(seconds));
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`;
}
