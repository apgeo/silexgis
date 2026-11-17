// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import process from 'node:process';

// What the browser-suite runner hands to the API it starts, and how it starts the Playwright CLI.

/**
 * Settings that say WHERE the API keeps its state or listens, which the runner decides for itself.
 *
 * The runner starts its own database container and points the API at it, and its header promises
 * that a run cannot migrate the shared development database. The API reads its settings from
 * unprefixed variables first and `SILEXGIS__` ones after, the later winning — so a
 * `SILEXGIS__Db__ConnectionString` exported in the developer's shell (the documented way to point
 * at a scratch database) used to override the runner's own unprefixed one, and the demo seeding
 * and the whole suite wrote into that other database. The same held for the file store, the key
 * ring and the terrain folders. Behaviour switches are not on this list: a caller choosing one for
 * a run (a spec documents being run with one; the photo-library wrapper hands its libraries over
 * that way) is the point of inheriting the environment.
 */
const RunnerOwned = [
  /^SILEXGIS__Db__/i,
  /^Db__/i,
  /^SILEXGIS__Files__/i,
  /^SILEXGIS__Keys__/i,
  /^SILEXGIS__Terrain__\w*Root$/i,
  /^ASPNETCORE_URLS$/i,
];

/** The caller's environment without the runner-owned settings, with the runner's own laid over it. */
export function forTheApi(inherited, own) {
  const kept = Object.fromEntries(
    Object.entries(inherited).filter(([key]) => !RunnerOwned.some((owned) => owned.test(key))));
  return { ...kept, ...own };
}

/**
 * The command that runs Playwright's test runner with `argv`: Node itself on Playwright's own
 * command-line script, never `npx`. On Windows `npx` is `npx.cmd`, which Node starts only through
 * cmd.exe, and a shell joins the arguments with spaces and quotes none of them — so a filter
 * passed through, such as `-g "a link is anchored"`, would reach Playwright as four words, and
 * `-g "a|b"` as a pipe into a program called `b`. Without a shell every argument arrives as given.
 */
export function playwrightCommand(clientDir, argv, nodePath = process.execPath) {
  return {
    command: nodePath,
    args: [join(clientDir, 'node_modules', '@playwright', 'test', 'cli.js'), 'test', ...argv],
  };
}

/**
 * The start time the kernel recorded for a process, or null where that cannot be read (no
 * /proc, or the process is gone). Kept beside a pid that has to outlive this run, because a pid
 * alone does not: after a reboot the number belongs to whatever process was given it.
 */
export function processStartTime(pid) {
  try {
    const stat = readFileSync(`/proc/${pid}/stat`, 'utf8');
    // Counted from the last ')' — the command name in parentheses may contain spaces.
    return stat.slice(stat.lastIndexOf(')') + 2).split(' ')[19] ?? null;
  } catch {
    return null;
  }
}

/**
 * Whether the pid a run left in its pid file may be stopped: only when the file recorded the
 * process's start time and the process with that pid still has it. A pid file without one, or a
 * platform where start times cannot be read, never authorises a kill — the port check that follows
 * then refuses with a message instead of this run stopping something that is not its own.
 */
export function leftoverApiPid(fileText, startTimeOf = processStartTime) {
  const [pidText, recorded] = fileText.trim().split(/\s+/);
  const pid = Number(pidText);
  if (!Number.isInteger(pid) || pid <= 0 || !recorded) return null;
  return startTimeOf(pid) === recorded ? pid : null;
}
