// SPDX-License-Identifier: AGPL-3.0-or-later
//
// What the native-install scripts share: where an installation lives, how its configuration is
// read and written, how PostgreSQL, the .NET host and the web server are driven, and how the API
// is started, stopped and waited for.
//
// Node 24, built-ins only, no shell: every external program is started with an argument list, so
// a path with a space in it or a password with a quote in it means what it says on Windows and on
// Linux alike. The pure parts (parsing, rendering, judging) are exported for `native.test.mjs`;
// the parts that touch the machine are kept thin around them.
//
// An installation is described by two files in its configuration directory:
//   - silexgis.env   what the API reads: SILEXGIS__* settings and ASPNETCORE_URLS, credentials
//                    included. The systemd unit's EnvironmentFile. Mode 600; never in the repo.
//   - install.json   what these scripts read: the layout and the choices made at install time.
//                    Holds no secret.

import { spawn, spawnSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const nativeDir = path.dirname(fileURLToPath(import.meta.url));
export const repoRoot = path.join(nativeDir, '..', '..');
export const isWindows = process.platform === 'win32';

/** The file the API's published build carries to say what it was built from. */
export const BUILD_MANIFEST = 'silexgis-build.json';
export const ENV_FILE = 'silexgis.env';
export const INSTALL_RECORD = 'install.json';
export const API_DLL = 'SilexGis.Api.dll';

// ---------------------------------------------------------------------------------------------
// Output
// ---------------------------------------------------------------------------------------------

export const say = (message) => console.log(`\n==> ${message}`);
export const note = (message) => console.log(`    ${message}`);
export const warn = (message) => console.warn(`    WARNING: ${message}`);

/** A failure an operator can act on: printed as one message, without a stack trace. */
export class Failure extends Error {}

export const fail = (message) => {
  throw new Failure(message);
};

/** Runs a script's main function and turns a Failure into a named message and exit code 1. */
export async function runMain(main) {
  try {
    const code = await main(process.argv.slice(2));
    process.exitCode = typeof code === 'number' ? code : 0;
  } catch (error) {
    if (error instanceof Failure) {
      console.error(`\nFAILED: ${error.message}`);
      process.exitCode = 1;
    } else if (String(error.code).startsWith('ERR_PARSE_ARGS')) {
      console.error(`${error.message} (--help lists the options)`);
      process.exitCode = 2;
    } else {
      console.error(error);
      process.exitCode = 1;
    }
  }
}

// ---------------------------------------------------------------------------------------------
// Layout and settings
// ---------------------------------------------------------------------------------------------

/**
 * Where an installation lives when nothing says otherwise.
 *
 * Linux follows the filesystem hierarchy the systemd unit was written for: the program under
 * /opt, what it writes under /var/lib, configuration under /etc. Windows splits the same way
 * between Program Files (the program) and ProgramData (everything it writes and reads as
 * configuration). Taken from the platform given rather than the one running, so both answers
 * can be tested anywhere.
 */
export function defaultLayout(platform = process.platform, env = process.env) {
  if (platform === 'win32') {
    const p = path.win32;
    const programFiles = env.ProgramFiles || 'C:\\Program Files';
    const programData = env.ProgramData || 'C:\\ProgramData';
    return {
      appDir: p.join(programFiles, 'SilexGIS'),
      dataDir: p.join(programData, 'SilexGIS', 'data'),
      configDir: p.join(programData, 'SilexGIS', 'config'),
      logDir: p.join(programData, 'SilexGIS', 'logs'),
      backupDir: p.join(programData, 'SilexGIS', 'backups'),
    };
  }
  return {
    appDir: '/opt/silexgis',
    dataDir: '/var/lib/silexgis',
    configDir: '/etc/silexgis',
    logDir: '/var/log/silexgis',
    backupDir: '/var/backups/silexgis',
  };
}

/**
 * Every setting that describes an installation's layout, with the command-line option and the
 * environment variable that set it. The environment names match the Docker path's update script
 * where the two mean the same thing (SILEXGIS_APP_DIR, SILEXGIS_BACKUP_DIR,
 * SILEXGIS_HEALTH_TIMEOUT), so an operator's habits carry over.
 */
export const LAYOUT_SETTINGS = {
  appDir: { option: 'app-dir', env: 'SILEXGIS_APP_DIR' },
  dataDir: { option: 'data-dir', env: 'SILEXGIS_DATA_DIR' },
  logDir: { option: 'log-dir', env: 'SILEXGIS_LOG_DIR' },
  backupDir: { option: 'backup-dir', env: 'SILEXGIS_BACKUP_DIR' },
  sourceDir: { option: 'source-dir', env: 'SILEXGIS_SOURCE_DIR' },
  publicUrl: { option: 'public-url', env: 'SILEXGIS_PUBLIC_URL' },
  serverName: { option: 'server-name', env: 'SILEXGIS_SERVER_NAME' },
  httpPort: { option: 'http-port', env: 'SILEXGIS_HTTP_PORT', type: 'port' },
  apiPort: { option: 'api-port', env: 'SILEXGIS_API_PORT', type: 'port' },
  service: { option: 'service', env: 'SILEXGIS_SERVICE', choices: ['systemd', 'process', 'none'] },
  serviceName: { option: 'service-name', env: 'SILEXGIS_SERVICE_NAME' },
  serviceUser: { option: 'service-user', env: 'SILEXGIS_SERVICE_USER' },
  webServer: { option: 'web-server', env: 'SILEXGIS_WEB_SERVER', choices: ['nginx', 'none'] },
  nginxConf: { option: 'nginx-conf', env: 'SILEXGIS_NGINX_CONF' },
  pgBin: { option: 'pg-bin', env: 'SILEXGIS_PG_BIN' },
};

/** The configuration directory is where the record is, so it cannot come from the record. */
export const CONFIG_DIR_SETTING = { option: 'config-dir', env: 'SILEXGIS_CONFIG_DIR' };

/** parseArgs option declarations for the layout settings, shared by every script. */
export function layoutOptionSpec() {
  const spec = { [CONFIG_DIR_SETTING.option]: { type: 'string' } };
  for (const { option } of Object.values(LAYOUT_SETTINGS)) spec[option] = { type: 'string' };
  return spec;
}

/**
 * One layout setting's value and where it came from.
 *
 * Precedence: the command line, then the environment, then what the installation recorded when
 * it was installed, then the default. `explicit` is true for the first two — the cases in which
 * an operator asked for something on this run, and the only cases in which a re-run may change
 * what an earlier run wrote.
 */
export function pick(values, env, recorded, key, setting, fallback) {
  const fromOption = values[setting.option];
  if (fromOption !== undefined && fromOption !== '') return { value: fromOption, explicit: true };
  const fromEnv = env[setting.env];
  if (fromEnv !== undefined && fromEnv !== '') return { value: fromEnv, explicit: true };
  if (recorded && recorded[key] !== undefined && recorded[key] !== null) {
    return { value: recorded[key], explicit: false };
  }
  return { value: fallback, explicit: false };
}

/** The configuration directory, from the command line, the environment or the default. */
export function resolveConfigDir(values, env = process.env, platform = process.platform) {
  return (
    values[CONFIG_DIR_SETTING.option] || env[CONFIG_DIR_SETTING.env] || defaultLayout(platform, env).configDir
  );
}

/** A TCP port, or a named failure. */
export function parsePort(value, name) {
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) fail(`${name} must be a port number, not "${value}"`);
  return port;
}

/**
 * The full layout of an installation.
 *
 * `platformFacts` carries what the machine is like (whether systemd runs, whether nginx is
 * installed, whether this is root) so the defaults that depend on it can be tested without one.
 */
export function resolveLayout(values, env, recorded, platformFacts) {
  const { platform, systemd, nginx, root } = platformFacts;
  const defaults = defaultLayout(platform, env);
  const join = platform === 'win32' ? path.win32.join : path.posix.join;
  const explicit = new Set();
  const take = (key, fallback) => {
    const { value, explicit: given } = pick(values, env, recorded, key, LAYOUT_SETTINGS[key], fallback);
    if (given) explicit.add(key);
    return value;
  };

  const layout = { configDir: resolveConfigDir(values, env, platform) };
  layout.appDir = take('appDir', defaults.appDir);
  layout.dataDir = take('dataDir', defaults.dataDir);
  layout.logDir = take('logDir', defaults.logDir);
  layout.backupDir = take('backupDir', defaults.backupDir);
  layout.sourceDir = take('sourceDir', repoRoot);
  layout.publicUrl = take('publicUrl', undefined);
  layout.httpPort = parsePort(take('httpPort', 80), '--http-port');
  layout.apiPort = parsePort(take('apiPort', 5080), '--api-port');
  layout.serverName = take('serverName', layout.publicUrl ? hostOf(layout.publicUrl) : undefined);
  layout.service = take('service', platform !== 'win32' && systemd && root ? 'systemd' : 'process');
  layout.serviceName = take('serviceName', 'silexgis-api');
  layout.serviceUser = take('serviceUser', platform !== 'win32' && root ? 'silexgis' : os.userInfo().username);
  layout.webServer = take('webServer', platform !== 'win32' && nginx ? 'nginx' : 'none');
  layout.nginxConf = take('nginxConf', undefined);
  layout.pgBin = take('pgBin', undefined);

  for (const [key, setting] of Object.entries(LAYOUT_SETTINGS)) {
    if (setting.choices && !setting.choices.includes(layout[key])) {
      fail(`--${setting.option} must be one of ${setting.choices.join(', ')}, not "${layout[key]}"`);
    }
  }

  layout.apiDir = join(layout.appDir, 'api');
  layout.webRoot = join(layout.appDir, 'web');
  layout.filesDir = join(layout.dataDir, 'files');
  layout.keysDir = join(layout.dataDir, 'keys');
  layout.terrainDir = join(layout.dataDir, 'terrain');
  layout.envFile = join(layout.configDir, ENV_FILE);
  layout.recordFile = join(layout.configDir, INSTALL_RECORD);
  layout.pidFile = join(layout.dataDir, 'api.pid');
  layout.apiLog = join(layout.logDir, 'api.log');
  layout.apiUrl = `http://127.0.0.1:${layout.apiPort}`;
  layout.explicit = explicit;
  return layout;
}

/** The host name of a URL, for the web server's server_name. */
export function hostOf(url) {
  try {
    return new URL(url).hostname;
  } catch {
    fail(`not a URL: "${url}"`);
  }
}

/** The fields of a layout worth recording for the next run. */
export function recordOf(layout, extra = {}) {
  const keys = Object.keys(LAYOUT_SETTINGS);
  const record = { version: 1 };
  for (const key of keys) if (layout[key] !== undefined) record[key] = layout[key];
  return { ...record, ...extra };
}

export function readRecord(recordFile) {
  if (!fs.existsSync(recordFile)) return undefined;
  try {
    return JSON.parse(fs.readFileSync(recordFile, 'utf8'));
  } catch (error) {
    fail(`${recordFile} is not readable as JSON (${error.message}); fix or remove it`);
  }
}

/**
 * The layout of an installation that already exists, for the scripts that run against one.
 * Refuses when there is no record: guessing at defaults would point a restore at a database and
 * a directory nobody confirmed are this installation's.
 */
export function loadInstallation(values, env = process.env) {
  const configDir = resolveConfigDir(values, env);
  const recordFile = path.join(configDir, INSTALL_RECORD);
  const recorded = readRecord(recordFile);
  if (!recorded) {
    fail(
      `no installation recorded at ${recordFile}. Install with deploy/native/install.mjs first, or ` +
        'name the configuration directory with --config-dir (or SILEXGIS_CONFIG_DIR).',
    );
  }
  const layout = resolveLayout(values, env, recorded, machineFacts());
  const envText = fs.existsSync(layout.envFile) ? fs.readFileSync(layout.envFile, 'utf8') : fail(`${layout.envFile} is missing`);
  const appEnv = parseEnvFile(envText).values;
  const cs = appEnv.SILEXGIS__Db__ConnectionString;
  if (!cs) fail(`${layout.envFile} sets no SILEXGIS__Db__ConnectionString`);
  return { layout, recorded, appEnv, db: parseConnectionString(cs) };
}

/** What the machine running this is like, as far as the defaults care. */
export function machineFacts() {
  return {
    platform: process.platform,
    systemd: !isWindows && fs.existsSync('/run/systemd/system'),
    nginx: !isWindows && findExecutable('nginx', ['/usr/sbin', '/usr/local/sbin']) !== undefined,
    root: !isWindows && typeof process.getuid === 'function' && process.getuid() === 0,
  };
}

// ---------------------------------------------------------------------------------------------
// The environment file
// ---------------------------------------------------------------------------------------------

/**
 * Reads a systemd-style environment file: `KEY=value` lines, `#` comments, blank lines, values
 * optionally in double quotes with `\"` and `\\` escaped, or in single quotes taken literally.
 * The API reads the same file through systemd on Linux, and through `startApi` in process mode,
 * so both read it the way this does.
 */
export function parseEnvFile(text) {
  const values = {};
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const eq = line.indexOf('=');
    if (eq <= 0) continue;
    const key = line.slice(0, eq).trim();
    values[key] = unquoteEnvValue(line.slice(eq + 1).trim());
  }
  return { values };
}

function unquoteEnvValue(value) {
  if (value.length >= 2 && value.startsWith('"') && value.endsWith('"')) {
    return value.slice(1, -1).replace(/\\(["\\])/g, '$1');
  }
  if (value.length >= 2 && value.startsWith("'") && value.endsWith("'")) return value.slice(1, -1);
  return value;
}

/** A value as one line of an environment file, quoted only when it has to be. */
export function formatEnvValue(value) {
  const text = String(value);
  if (/[\r\n]/.test(text)) fail('a configuration value cannot span lines');
  if (text === '' || /^[A-Za-z0-9_.,:;=/@+%~^-]+$/.test(text)) return text;
  return `"${text.replace(/(["\\])/g, '\\$1')}"`;
}

/**
 * Merges settings into an environment file's text, keeping every line it does not change.
 *
 * Each update is `{ value, force }`. A key already in the file keeps its value unless `force` —
 * which is how a re-run converges without minting a new password or undoing an operator's edit,
 * while an option given explicitly on this run still wins. Keys not in the file are appended.
 */
export function mergeEnvFile(text, updates) {
  const lines = text === '' ? [] : text.replace(/\r\n/g, '\n').replace(/\n$/, '').split('\n');
  const seen = new Set();
  const out = lines.map((line) => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) return line;
    const eq = trimmed.indexOf('=');
    if (eq <= 0) return line;
    const key = trimmed.slice(0, eq).trim();
    const update = updates[key];
    if (!update) return line;
    if (seen.has(key)) return line;
    seen.add(key);
    return update.force ? `${key}=${formatEnvValue(update.value)}` : line;
  });
  const appended = Object.entries(updates).filter(([key]) => !seen.has(key));
  if (appended.length > 0) {
    if (out.length > 0 && out[out.length - 1].trim() !== '') out.push('');
    for (const [key, update] of appended) {
      if (update.comment) out.push(`# ${update.comment}`);
      out.push(`${key}=${formatEnvValue(update.value)}`);
    }
  }
  return `${out.join('\n')}\n`;
}

/** Writes a file readable by its owner only, where the platform has such a thing. */
export function writePrivateFile(file, text) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text, { mode: 0o600 });
  if (!isWindows) fs.chmodSync(file, 0o600);
}

/** A password made of letters and digits only, so it needs no quoting anywhere it is written. */
export function generateSecret(length = 32) {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789';
  const bytes = randomBytes(length * 2);
  let out = '';
  for (const byte of bytes) {
    // Rejection sampling keeps every character equally likely.
    if (byte < 256 - (256 % alphabet.length)) out += alphabet[byte % alphabet.length];
    if (out.length === length) break;
  }
  return out.length === length ? out : generateSecret(length);
}

// ---------------------------------------------------------------------------------------------
// The database connection
// ---------------------------------------------------------------------------------------------

const CONNECTION_KEYS = {
  host: 'host',
  server: 'host',
  port: 'port',
  database: 'database',
  db: 'database',
  username: 'username',
  'user id': 'username',
  userid: 'username',
  user: 'username',
  password: 'password',
  pwd: 'password',
};

/**
 * Reads the parts of an Npgsql connection string these scripts need.
 *
 * Npgsql's own syntax: `key=value` pairs separated by `;`, a value optionally in single or double
 * quotes with the quote doubled inside. Keys it does not need are kept in `extra` so a string
 * written back out loses nothing an operator added (SSL Mode, Timeout, …).
 */
export function parseConnectionString(text) {
  const result = { host: 'localhost', port: 5432, extra: [] };
  let i = 0;
  while (i < text.length) {
    while (i < text.length && (text[i] === ';' || text[i] === ' ')) i++;
    if (i >= text.length) break;
    const eq = text.indexOf('=', i);
    if (eq === -1) fail(`connection string is not key=value pairs near "${text.slice(i, i + 20)}"`);
    const rawKey = text.slice(i, eq).trim();
    i = eq + 1;
    let value = '';
    if (text[i] === "'" || text[i] === '"') {
      const quote = text[i++];
      while (i < text.length) {
        if (text[i] === quote && text[i + 1] === quote) {
          value += quote;
          i += 2;
        } else if (text[i] === quote) {
          i++;
          break;
        } else {
          value += text[i++];
        }
      }
      while (i < text.length && text[i] !== ';') i++;
    } else {
      const end = text.indexOf(';', i);
      value = (end === -1 ? text.slice(i) : text.slice(i, end)).trim();
      i = end === -1 ? text.length : end;
    }
    const known = CONNECTION_KEYS[rawKey.toLowerCase()];
    if (known === 'port') result.port = Number(value);
    else if (known) result[known] = value;
    else result.extra.push([rawKey, value]);
  }
  return result;
}

/** One value of an Npgsql connection string, quoted when it would otherwise be misread. */
export function formatConnectionValue(value) {
  const text = String(value);
  if (!/[;'"=\s]/.test(text) && text !== '') return text;
  return `'${text.replace(/'/g, "''")}'`;
}

export function buildConnectionString({ host, port, database, username, password, extra = [] }) {
  const pairs = [
    ['Host', host],
    ['Port', port],
    ['Database', database],
    ['Username', username],
    ['Password', password],
    ...extra,
  ].filter(([, value]) => value !== undefined && value !== '');
  return pairs.map(([key, value]) => `${key}=${formatConnectionValue(value)}`).join(';');
}

/**
 * The libpq environment for a connection. Credentials travel in the environment rather than on
 * the command line, where every other account on the machine could read them in a process list.
 */
export function pgEnv(conn) {
  const env = { PGHOST: conn.host, PGPORT: String(conn.port), PGDATABASE: conn.database, PGUSER: conn.username };
  if (conn.password !== undefined) env.PGPASSWORD = conn.password;
  // A client that offers a password nobody configured must not then sit waiting for one typed.
  env.PGCONNECT_TIMEOUT = '10';
  return env;
}

/** A `postgres://user:password@host:port/database` URL as a connection. */
export function parsePostgresUrl(url) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    fail('--db-admin-url is not a URL (postgres://user:password@host:5432/postgres)');
  }
  if (!['postgres:', 'postgresql:'].includes(parsed.protocol)) {
    fail('--db-admin-url must start with postgres:// or postgresql://');
  }
  return {
    host: parsed.searchParams.get('host') || decodeURIComponent(parsed.hostname) || 'localhost',
    port: Number(parsed.port || 5432),
    database: decodeURIComponent(parsed.pathname.replace(/^\//, '')) || 'postgres',
    username: decodeURIComponent(parsed.username) || 'postgres',
    password: parsed.password ? decodeURIComponent(parsed.password) : undefined,
  };
}

/** An SQL string literal. Standard-conforming strings are the default since PostgreSQL 9.1. */
export const sqlLiteral = (value) => `'${String(value).replace(/'/g, "''")}'`;

/** An SQL identifier. */
export const sqlIdent = (value) => `"${String(value).replace(/"/g, '""')}"`;

export const isLocalHost = (host) =>
  !host || host === 'localhost' || host === '127.0.0.1' || host === '::1' || host.startsWith('/');

/**
 * The errors a restore may meet and carry on past. Exactly those the Docker path's restore
 * lets through: the PostGIS image creates the geocoder's and topology's schemas itself, only
 * `public` is dropped before the load, and the dump's CREATE SCHEMA for each then finds it there.
 * Every other error fails the restore, because psql goes on past an error and still exits 0.
 */
const EXPECTED_RESTORE_ERROR = /ERROR: +schema "(tiger|tiger_data|topology)" already exists/;

export function unexpectedRestoreErrors(stderr) {
  return stderr
    .split(/\r?\n/)
    .filter((line) => line.includes('ERROR:'))
    .filter((line) => !EXPECTED_RESTORE_ERROR.test(line));
}

// ---------------------------------------------------------------------------------------------
// Programs
// ---------------------------------------------------------------------------------------------

/** Finds a program on PATH (and in `extraDirs`), the way a shell would, without one. */
export function findExecutable(name, extraDirs = [], env = process.env) {
  const dirs = [...(env.PATH || env.Path || '').split(path.delimiter), ...extraDirs].filter(Boolean);
  const extensions = isWindows ? (env.PATHEXT || '.EXE;.CMD;.BAT').split(';') : [''];
  for (const dir of dirs) {
    for (const ext of extensions) {
      const candidate = path.join(dir, name + ext.toLowerCase());
      const candidateUpper = path.join(dir, name + ext);
      for (const file of new Set([candidate, candidateUpper])) {
        try {
          const stat = fs.statSync(file);
          if (stat.isFile()) {
            if (!isWindows) fs.accessSync(file, fs.constants.X_OK);
            return file;
          }
        } catch {
          // not here
        }
      }
    }
  }
  return undefined;
}

/** A PostgreSQL client program, from --pg-bin when given. */
export function pgProgram(layout, name) {
  if (layout.pgBin) return path.join(layout.pgBin, isWindows ? `${name}.exe` : name);
  return findExecutable(name) ?? name;
}

/**
 * Runs a program to completion. Output goes to the terminal unless `capture`, in which case it
 * is returned. A non-zero exit is a Failure naming the program unless `allowFail`.
 */
export function run(command, args, options = {}) {
  const { cwd, env, exactEnv, input, capture = false, allowFail = false, shell = false, label, uid, gid } = options;
  const result = spawnSync(command, args, {
    cwd,
    env: exactEnv ?? (env ? { ...process.env, ...env } : process.env),
    ...(uid !== undefined ? { uid, gid } : {}),
    input,
    shell,
    encoding: 'utf8',
    stdio: capture ? ['pipe', 'pipe', 'pipe'] : [input === undefined ? 'inherit' : 'pipe', 'inherit', 'inherit'],
    maxBuffer: 256 * 1024 * 1024,
    windowsHide: true,
  });
  if (result.error) {
    if (allowFail) return { status: -1, stdout: '', stderr: String(result.error.message), error: result.error };
    fail(`${label ?? command} could not be started: ${result.error.message}`);
  }
  if (result.status !== 0 && !allowFail) {
    const detail = capture ? `\n${(result.stderr || result.stdout || '').trim()}` : '';
    fail(`${label ?? `${command} ${args.join(' ')}`} exited with ${result.status ?? result.signal}${detail}`);
  }
  return { status: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '' };
}

/** npm is a batch file on Windows, which Node will only start through a shell. */
export function runNpm(args, options) {
  return run('npm', args, { ...options, shell: isWindows, label: `npm ${args.join(' ')}` });
}

// ---------------------------------------------------------------------------------------------
// Versions
// ---------------------------------------------------------------------------------------------

/** The first `major.minor[.patch]` in a string, as numbers. */
export function parseVersion(text) {
  const match = /(\d+)\.(\d+)(?:\.(\d+))?/.exec(String(text));
  return match ? [Number(match[1]), Number(match[2]), Number(match[3] ?? 0)] : undefined;
}

export function versionAtLeast(actual, wanted) {
  for (let i = 0; i < 3; i++) {
    const a = actual[i] ?? 0;
    const w = wanted[i] ?? 0;
    if (a !== w) return a > w;
  }
  return true;
}

/** The .NET major version the API targets, read from its project rather than written here twice. */
export function requiredDotnetMajor(csprojText) {
  const match = /<TargetFramework>net(\d+)\.\d+<\/TargetFramework>/.exec(csprojText);
  if (!match) fail('the API project names no TargetFramework this script can read');
  return Number(match[1]);
}

/** The Node major version the client declares, read from its package.json. */
export function requiredNodeMajor(packageJsonText) {
  const engines = JSON.parse(packageJsonText).engines?.node ?? '';
  const match = />=\s*(\d+)/.exec(engines);
  return match ? Number(match[1]) : undefined;
}

/** Whether `dotnet --list-sdks` / `--list-runtimes` output carries the major version wanted. */
export function dotnetHas(listing, major, runtimeName) {
  return listing
    .split(/\r?\n/)
    .some((line) => {
      const trimmed = line.trim();
      if (runtimeName) {
        if (!trimmed.startsWith(`${runtimeName} `)) return false;
        return parseVersion(trimmed.slice(runtimeName.length))?.[0] === major;
      }
      return parseVersion(trimmed)?.[0] === major;
    });
}

/** PostgreSQL's server_version_num (150004) as [15, 4, 0]. */
export function pgVersionFromNum(num) {
  const n = Number(num);
  return [Math.floor(n / 10000), n % 10000, 0];
}

// ---------------------------------------------------------------------------------------------
// Migrations
// ---------------------------------------------------------------------------------------------

/**
 * The migration identifiers a source tree defines, read from the attribute EF Core records them
 * under — the same string `__EFMigrationsHistory` holds once one is applied.
 */
export function migrationIdsFromSource(sourceDir) {
  const dir = path.join(sourceDir, 'server', 'src', 'SilexGis.Infrastructure', 'Migrations');
  if (!fs.existsSync(dir)) return [];
  const ids = [];
  for (const name of fs.readdirSync(dir)) {
    if (!name.endsWith('.Designer.cs')) continue;
    const match = /\[Migration\("([^"]+)"\)\]/.exec(fs.readFileSync(path.join(dir, name), 'utf8'));
    if (match) ids.push(match[1]);
  }
  return ids.sort();
}

/** What a build defines that a database has not applied. */
export const missingMigrations = (defined, applied) => {
  const have = new Set(applied);
  return defined.filter((id) => !have.has(id));
};

// ---------------------------------------------------------------------------------------------
// Rendering the web server and service configuration
// ---------------------------------------------------------------------------------------------

/**
 * Replaces each literal in a template, insisting that every one is found.
 *
 * The templates are real configuration files that an operator can also read and copy by hand,
 * not files full of placeholders, so the installer substitutes the example values they carry.
 * A literal that is no longer there means the template was edited under the installer; carrying
 * on would install a file still pointing at the example host or directory, which starts and
 * serves the wrong thing. So it stops instead, naming what it could not find.
 */
export function substituteLiterals(template, replacements, templateName) {
  let text = template;
  for (const [literal, replacement, { all = false } = {}] of replacements) {
    if (!text.includes(literal)) fail(`${templateName} no longer contains "${literal}"; the installer cannot adapt it`);
    text = all ? text.split(literal).join(replacement) : text.replace(literal, () => replacement);
  }
  return text;
}

const toPosix = (p) => p.split(path.sep).join('/');

/** The standalone nginx server block for this installation. */
export function renderNginx(template, layout) {
  const header =
    '# Rendered by deploy/native/install.mjs from deploy/nginx/silexgis.conf for this installation.\n' +
    '# Edit it as you need: a later install or update leaves a file it did not write alone and puts\n' +
    '# its own beside it as .new for you to compare.\n\n';
  return (
    header +
    substituteLiterals(
      template,
      [
        ['    listen 80;', `    listen ${layout.httpPort};`],
        ['    server_name caves.example.org;', `    server_name ${layout.serverName};`],
        ['    root /var/www/silexgis;', `    root ${toPosix(layout.webRoot)};`],
        ['http://127.0.0.1:5080', layout.apiUrl, { all: true }],
        [
          'alias /var/lib/silexgis/terrain/published/;',
          `alias ${toPosix(path.join(layout.terrainDir, 'published'))}/;`,
        ],
      ],
      'deploy/nginx/silexgis.conf',
    )
  );
}

const unitPath = (p) => (/\s/.test(p) ? `"${p}"` : p);

/** The systemd unit for this installation. */
export function renderUnit(template, layout, dotnetPath) {
  const dll = path.posix.join(layout.apiDir, API_DLL);
  const header =
    '# Rendered by deploy/native/install.mjs from deploy/systemd/silexgis-api.service.\n' +
    '# Change it with `systemctl edit` (a drop-in survives re-installs); a later install leaves an\n' +
    '# edited file alone and puts its own beside it as .new.\n\n';
  return (
    header +
    substituteLiterals(
      template,
      [
        ['User=silexgis', `User=${layout.serviceUser}`],
        ['Group=silexgis', `Group=${layout.serviceUser}`],
        ['WorkingDirectory=/opt/silexgis/api', `WorkingDirectory=${unitPath(layout.apiDir)}`],
        [
          'ExecStart=/usr/bin/dotnet /opt/silexgis/api/SilexGis.Api.dll',
          `ExecStart=${unitPath(dotnetPath)} ${unitPath(dll)}`,
        ],
        ['Environment=ASPNETCORE_URLS=http://127.0.0.1:5080', `Environment=ASPNETCORE_URLS=${layout.apiUrl}`],
        ['EnvironmentFile=/etc/silexgis/silexgis.env', `EnvironmentFile=${unitPath(layout.envFile)}`],
        ['ReadWritePaths=/var/lib/silexgis', `ReadWritePaths=${unitPath(layout.dataDir)}`],
      ],
      'deploy/systemd/silexgis-api.service',
    )
  );
}

/** The nightly backup a systemd installation gets, as a service and its timer. */
export function renderBackupUnits(layout, nodePath, keep) {
  const script = path.posix.join(layout.sourceDir, 'deploy', 'native', 'backup.mjs');
  const service = [
    '# Written by deploy/native/install.mjs.',
    '[Unit]',
    'Description=SilexGIS database and file-store backup',
    '',
    '[Service]',
    'Type=oneshot',
    `ExecStart=${unitPath(nodePath)} ${unitPath(script)} --config-dir ${unitPath(layout.configDir)} --keep ${keep}`,
    '',
  ].join('\n');
  const timer = [
    '# Written by deploy/native/install.mjs.',
    '[Unit]',
    'Description=Nightly SilexGIS backup',
    '',
    '[Timer]',
    'OnCalendar=*-*-* 03:20:00',
    'RandomizedDelaySec=10m',
    'Persistent=true',
    '',
    '[Install]',
    'WantedBy=timers.target',
    '',
  ].join('\n');
  return { service, timer };
}

export const sha256 = (text) => createHash('sha256').update(text).digest('hex');

/**
 * Installs a rendered configuration file the way a package manager installs one: written when
 * absent, or when it is still exactly what this installer wrote last time; otherwise left alone,
 * with the new rendering put beside it as `.new`. A certificate tool that added TLS to the nginx
 * site, or an operator who tuned the unit, keeps their change through every re-run and update.
 *
 * Returns `{ changed, kept, hash, previous }`; `hash` is what to record as written.
 */
export function installConfigFile(file, text, lastWrittenHash) {
  const hash = sha256(text);
  if (fs.existsSync(file)) {
    const current = fs.readFileSync(file, 'utf8');
    const currentHash = sha256(current);
    if (currentHash === hash) return { changed: false, kept: false, hash, previous: current };
    if (lastWrittenHash && currentHash !== lastWrittenHash) {
      fs.writeFileSync(`${file}.new`, text);
      return { changed: false, kept: true, hash: lastWrittenHash, previous: current };
    }
    if (!lastWrittenHash) {
      // A file this installer has no record of writing: somebody else's. Kept, as above.
      fs.writeFileSync(`${file}.new`, text);
      return { changed: false, kept: true, hash: undefined, previous: current };
    }
    fs.writeFileSync(file, text);
    return { changed: true, kept: false, hash, previous: current };
  }
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
  return { changed: true, kept: false, hash, previous: undefined };
}

// ---------------------------------------------------------------------------------------------
// HTTP
// ---------------------------------------------------------------------------------------------

/**
 * A GET that can name the Host it asks for — which `fetch` cannot — and follows redirects.
 *
 * Needed because the web server picks its site by Host: checking an installation through
 * 127.0.0.1 has to say which name it is asking about, or the machine's default site answers and
 * the check reports on something else entirely.
 */
export function httpGet(url, { hostHeader, timeoutMs = 15_000, redirects = 5 } = {}) {
  return new Promise((resolve, reject) => {
    const target = new URL(url);
    const client = target.protocol === 'https:' ? https : http;
    const headers = { Accept: '*/*', 'User-Agent': 'silexgis-native-check' };
    if (hostHeader) headers.Host = hostHeader;
    const req = client.get(target, { headers, timeout: timeoutMs }, (res) => {
      const chunks = [];
      res.on('data', (chunk) => chunks.push(chunk));
      res.on('end', () => {
        const status = res.statusCode ?? 0;
        if ([301, 302, 303, 307, 308].includes(status) && res.headers.location && redirects > 0) {
          const next = new URL(res.headers.location, target).href;
          resolve(httpGet(next, { timeoutMs, redirects: redirects - 1 }));
          return;
        }
        resolve({ status, headers: res.headers, body: Buffer.concat(chunks), url: target.href });
      });
      res.on('error', reject);
    });
    req.on('timeout', () => req.destroy(new Error(`no answer from ${url} within ${timeoutMs / 1000}s`)));
    req.on('error', reject);
  });
}

export const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Whether a /health/ready answer says the API and its database are ready. */
export const isHealthy = (status, body) => status === 200 && /healthy/i.test(body) && !/unhealthy/i.test(body);

// ---------------------------------------------------------------------------------------------
// The service: starting, stopping and waiting for the API
// ---------------------------------------------------------------------------------------------

/** The numeric ids of an account, or undefined where the platform has none. */
export function accountIds(user) {
  if (isWindows) return undefined;
  const uid = run('id', ['-u', user], { capture: true, allowFail: true });
  const gid = run('id', ['-g', user], { capture: true, allowFail: true });
  if (uid.status !== 0 || gid.status !== 0) return undefined;
  return { uid: Number(uid.stdout.trim()), gid: Number(gid.stdout.trim()) };
}

const pidAlive = (pid) => {
  // 0 and negative numbers name process groups, not a process: never "alive" here.
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return error.code === 'EPERM';
  }
};

/**
 * The process-mode API's pid, if the recorded one is still the API. On Linux the command line is
 * read back, so a pid the system has since given to something else is never signalled.
 */
export function runningPid(layout) {
  if (!fs.existsSync(layout.pidFile)) return undefined;
  const pid = Number(fs.readFileSync(layout.pidFile, 'utf8').trim());
  if (!Number.isInteger(pid) || pid <= 0 || !pidAlive(pid)) return undefined;
  if (process.platform === 'linux') {
    try {
      const cmdline = fs.readFileSync(`/proc/${pid}/cmdline`, 'utf8');
      if (!cmdline.includes(API_DLL)) return undefined;
    } catch {
      return undefined;
    }
  }
  return pid;
}

/** The environment the API runs with in process mode: this one's, minus its own settings, plus the file. */
export function apiEnvironment(baseEnv, fileValues, layout) {
  const env = {};
  for (const [key, value] of Object.entries(baseEnv)) {
    if (key.startsWith('SILEXGIS__') || key === 'ASPNETCORE_URLS' || key.startsWith('SILEXGIS_')) continue;
    env[key] = value;
  }
  env.ASPNETCORE_URLS = layout.apiUrl;
  Object.assign(env, fileValues);
  if (!isWindows) env.HOME = layout.dataDir;
  return env;
}

export async function stopApi(layout) {
  if (layout.service === 'systemd') {
    run('systemctl', ['stop', layout.serviceName], { allowFail: true });
    return;
  }
  if (layout.service === 'none') return;
  const pid = runningPid(layout);
  if (!pid) return;
  process.kill(pid, 'SIGTERM');
  // The host's own shutdown allowance is 30 s; this waits a little longer than that before ending
  // the process, as systemd's stop timeout and Docker's stop would.
  for (let i = 0; i < 90 && pidAlive(pid); i++) await sleep(500);
  if (pidAlive(pid)) {
    note(`the API (pid ${pid}) had not finished stopping after 45 s; ending it`);
    process.kill(pid, 'SIGKILL');
    await sleep(1000);
  }
  fs.rmSync(layout.pidFile, { force: true });
}

/**
 * Starts the API the way the installation runs it.
 *
 * `process` mode is a detached child, for a machine with no service manager — a container, a
 * Windows host without a service wrapper, a continuous-integration runner. It runs as the
 * service account when started by root, writes to the log directory and leaves its pid where
 * stop and update will find it. It does not survive a reboot; that is what systemd is for.
 */
export async function startApi(layout) {
  if (layout.service === 'systemd') {
    run('systemctl', ['restart', layout.serviceName]);
    return;
  }
  if (layout.service === 'none') {
    note(`service mode is "none": start the API yourself (dotnet ${path.join(layout.apiDir, API_DLL)})`);
    return;
  }
  await stopApi(layout);
  const dotnet = findExecutable('dotnet') ?? fail('dotnet is not on PATH');
  const fileValues = parseEnvFile(fs.readFileSync(layout.envFile, 'utf8')).values;
  fs.mkdirSync(layout.logDir, { recursive: true });
  const log = fs.openSync(layout.apiLog, 'a');
  const ids = machineFacts().root ? accountIds(layout.serviceUser) : undefined;
  const child = spawn(dotnet, [path.join(layout.apiDir, API_DLL)], {
    cwd: layout.apiDir,
    env: apiEnvironment(process.env, fileValues, layout),
    detached: true,
    stdio: ['ignore', log, log],
    windowsHide: true,
    ...(ids ?? {}),
  });
  child.unref();
  fs.closeSync(log);
  fs.writeFileSync(layout.pidFile, `${child.pid}\n`);
  note(`API started as a plain process (pid ${child.pid}), logging to ${layout.apiLog}`);
}

/** The last lines the API wrote, for a failure message. */
export function apiLogTail(layout, lines = 60) {
  if (layout.service === 'systemd') {
    return run('journalctl', ['-u', layout.serviceName, '-n', String(lines), '--no-pager'], {
      capture: true,
      allowFail: true,
    }).stdout;
  }
  if (!fs.existsSync(layout.apiLog)) return '(no log written)';
  return fs.readFileSync(layout.apiLog, 'utf8').split(/\r?\n/).slice(-lines).join('\n');
}

/** Waits for the API's own readiness answer, giving up early if a process-mode API has died. */
export async function waitHealthy(layout, timeoutSeconds) {
  const deadline = Date.now() + timeoutSeconds * 1000;
  const url = `${layout.apiUrl}/health/ready`;
  let last = 'no answer yet';
  while (Date.now() < deadline) {
    try {
      const res = await httpGet(url, { timeoutMs: 10_000 });
      const body = res.body.toString('utf8');
      if (isHealthy(res.status, body)) return { ok: true };
      last = `${res.status} ${body.slice(0, 120)}`;
    } catch (error) {
      last = error.message;
    }
    if (layout.service === 'process' && !runningPid(layout)) return { ok: false, reason: 'the API process exited' };
    await sleep(3000);
  }
  return { ok: false, reason: `not ready within ${timeoutSeconds}s (${last})` };
}

export const healthTimeout = (env = process.env) => Number(env.SILEXGIS_HEALTH_TIMEOUT || 300);

// ---------------------------------------------------------------------------------------------
// Files
// ---------------------------------------------------------------------------------------------

/** Every file under a directory with the given extension, as paths relative to it. */
export function filesWithExtension(dir, extension) {
  const out = [];
  const walk = (current) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith(extension)) out.push(path.relative(dir, full));
    }
  };
  if (fs.existsSync(dir)) walk(dir);
  return out;
}

/** Empties a directory without removing it, so its ownership and mode stay what they were. */
export function emptyDirectory(dir) {
  fs.mkdirSync(dir, { recursive: true });
  for (const entry of fs.readdirSync(dir)) fs.rmSync(path.join(dir, entry), { recursive: true, force: true });
}

/** Hands a tree to the service account (Linux, as root); a no-op elsewhere. */
export function chownTree(dir, user) {
  if (isWindows || !machineFacts().root) return;
  run('chown', ['-R', `${user}:${user}`, dir]);
}

/** A UTC timestamp for a backup directory, the same shape the Docker path uses. */
export const backupStamp = (date = new Date()) => date.toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');

/**
 * The backup directories beyond the newest `keep`. Only names in the timestamp shape count, so
 * a directory an operator put there by hand is never deleted.
 */
export function backupsToPrune(names, keep) {
  if (!keep || keep <= 0) return [];
  return names
    .filter((name) => /^\d{8}T\d{6}Z$/.test(name))
    .sort()
    .reverse()
    .slice(keep);
}

// ---------------------------------------------------------------------------------------------
// PostgreSQL
// ---------------------------------------------------------------------------------------------

const RUNUSER_DIRS = ['/usr/sbin', '/sbin', '/usr/bin', '/bin'];

/** The environment with every libpq variable removed, so a peer connection is not redirected. */
const withoutPgEnv = (env) => Object.fromEntries(Object.entries(env).filter(([key]) => !key.startsWith('PG')));

/**
 * How to start psql against a target, without starting it: `{ command, args, env, cwd }`.
 *
 * Three kinds of target. `app` is the application's own role, from its connection string. `url`
 * is an administrative connection the operator named with --db-admin-url. `peer` is the local
 * `postgres` account through the operating system's own authentication — what a Debian or Ubuntu
 * host offers root out of the box, and no password is involved at all.
 */
export function psqlCommand(layout, target, { database, extraArgs = [] } = {}) {
  const psql = pgProgram(layout, 'psql');
  const base = ['-X', ...extraArgs];
  if (target.kind === 'peer') {
    const runuser = findExecutable('runuser', RUNUSER_DIRS) ?? fail('runuser is not available');
    return {
      command: runuser,
      args: ['-u', 'postgres', '--', psql, ...base, '-d', database ?? 'postgres'],
      env: withoutPgEnv(process.env),
      // The postgres account may not be able to read the directory this was started from.
      cwd: '/',
    };
  }
  const conn = { ...target.conn, database: database ?? target.conn.database };
  return { command: psql, args: [...base, '--no-password'], env: { ...withoutPgEnv(process.env), ...pgEnv(conn) } };
}

/** Runs SQL (given on standard input, never on the command line) and returns its output. */
export function psqlRun(layout, target, { database, sql, allowFail = false, stopOnError = true }) {
  const spec = psqlCommand(layout, target, {
    database,
    extraArgs: ['-q', '-tA', '-v', `ON_ERROR_STOP=${stopOnError ? 1 : 0}`],
  });
  return run(spec.command, spec.args, {
    exactEnv: spec.env,
    cwd: spec.cwd,
    input: sql,
    capture: true,
    allowFail,
    label: `psql (${target.kind === 'app' ? `as ${target.conn.username}` : target.kind === 'peer' ? 'as postgres' : 'administrative'})`,
  });
}

/** The value of a one-row, one-column query, or a Failure naming the question. */
export function psqlValue(layout, target, database, sql) {
  return psqlRun(layout, target, { database, sql }).stdout.trim();
}

/**
 * Administrative access to the server, when there is any: the URL the operator gave, else the
 * local postgres account when this runs as root on the database's own machine. Undefined when
 * neither works — then only what the application's role may do can be done.
 */
export function adminAccess(layout, db, adminUrl) {
  if (adminUrl) {
    const target = { kind: 'url', conn: parsePostgresUrl(adminUrl) };
    const probe = psqlRun(layout, target, { database: target.conn.database, sql: 'select 1', allowFail: true });
    if (probe.status !== 0) fail(`--db-admin-url does not connect: ${probe.stderr.trim()}`);
    return target;
  }
  if (isWindows || !machineFacts().root || !isLocalHost(db.host)) return undefined;
  if (!findExecutable('runuser', RUNUSER_DIRS) || !accountIds('postgres')) return undefined;
  const target = { kind: 'peer' };
  const probe = psqlRun(layout, target, { database: 'postgres', sql: 'select 1', allowFail: true });
  return probe.status === 0 ? target : undefined;
}

/**
 * Creates the application's role and database, and the extensions it needs, with administrative
 * rights. Converges: an existing role gets the password the configuration holds, an existing
 * database is kept, an extension already there is left as it is.
 */
export function ensureDatabase(layout, admin, db) {
  const role = sqlIdent(db.username);
  psqlRun(layout, admin, {
    database: admin.kind === 'url' ? admin.conn.database : 'postgres',
    sql:
      `DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = ${sqlLiteral(db.username)}) ` +
      `THEN CREATE ROLE ${role} LOGIN; END IF; END $$;\n` +
      `ALTER ROLE ${role} WITH LOGIN PASSWORD ${sqlLiteral(db.password)};\n`,
  });
  note(`role ${db.username}: present, password set from the configuration`);

  const adminDb = admin.kind === 'url' ? admin.conn.database : 'postgres';
  const owner = psqlValue(
    layout,
    admin,
    adminDb,
    `SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = ${sqlLiteral(db.database)}`,
  );
  if (!owner) {
    psqlRun(layout, admin, { database: adminDb, sql: `CREATE DATABASE ${sqlIdent(db.database)} OWNER ${role};` });
    note(`database ${db.database}: created, owned by ${db.username}`);
  } else if (owner !== db.username) {
    warn(
      `database ${db.database} exists and is owned by ${owner}, not ${db.username}. Left as it is; ` +
        `the application needs to own it to run its migrations (ALTER DATABASE ${db.database} OWNER TO ${db.username}).`,
    );
  } else {
    note(`database ${db.database}: present, owned by ${db.username}`);
  }

  psqlRun(layout, admin, {
    database: db.database,
    sql: REQUIRED_EXTENSIONS.map((name) => `CREATE EXTENSION IF NOT EXISTS ${name};`).join('\n'),
  });
  note(`extensions ${REQUIRED_EXTENSIONS.join(', ')}: present`);
}

export const REQUIRED_EXTENSIONS = ['postgis', 'unaccent', 'ltree'];
export const MIN_POSTGRES = [15, 0, 0];
export const MIN_POSTGIS = [3, 3, 0];

/**
 * What the application's own role finds in its database, as problems to report: the server's
 * version, whether each extension is installed or at least available, and whether the role can
 * create what it will need to.
 */
export function databaseProblems({ serverVersionNum, extensions, canCreate }) {
  const problems = [];
  const server = pgVersionFromNum(serverVersionNum);
  if (!versionAtLeast(server, MIN_POSTGRES)) {
    problems.push(`PostgreSQL ${server[0]} is too old: 15 or later is required`);
  }
  for (const name of REQUIRED_EXTENSIONS) {
    const ext = extensions[name];
    if (!ext) {
      problems.push(
        name === 'postgis'
          ? 'PostGIS is not available on the database server: install it (on Debian or Ubuntu, postgresql-<version>-postgis-3)'
          : `the ${name} extension is not available on the database server (it ships with PostgreSQL's contrib modules)`,
      );
      continue;
    }
    if (name === 'postgis') {
      const version = parseVersion(ext.installed ?? ext.available);
      if (!version || !versionAtLeast(version, MIN_POSTGIS)) {
        problems.push(`PostGIS ${ext.installed ?? ext.available} is too old: 3.3 or later is required`);
      }
    }
    if (!ext.installed && name === 'postgis') {
      problems.push(
        'PostGIS is not installed in the database, and the application cannot install it itself. As a ' +
          `superuser, run: CREATE EXTENSION postgis; (or re-run with --db-admin-url, or as root on the database's machine)`,
      );
    }
  }
  if (!canCreate) problems.push("the application's role cannot create tables in the public schema; it should own the database");
  return problems;
}

/** Reads, as the application's role, what `databaseProblems` judges. */
export function inspectDatabase(layout, db) {
  const target = { kind: 'app', conn: db };
  const connect = psqlRun(layout, target, { database: db.database, sql: 'select 1', allowFail: true });
  if (connect.status !== 0) {
    fail(`the application's role cannot connect to ${db.database} on ${db.host}:${db.port} as ${db.username}: ${connect.stderr.trim()}`);
  }
  const serverVersionNum = psqlValue(layout, target, db.database, 'SHOW server_version_num');
  const rows = psqlValue(
    layout,
    target,
    db.database,
    `SELECT name || '|' || coalesce(default_version, '') || '|' || coalesce(installed_version, '') ` +
      `FROM pg_available_extensions WHERE name IN (${REQUIRED_EXTENSIONS.map(sqlLiteral).join(', ')})`,
  );
  const extensions = {};
  for (const line of rows.split(/\r?\n/).filter(Boolean)) {
    const [name, available, installed] = line.split('|');
    extensions[name] = { available: available || undefined, installed: installed || undefined };
  }
  const canCreate = psqlValue(layout, target, db.database, "SELECT has_schema_privilege('public', 'CREATE')") === 't';
  return { serverVersionNum, extensions, canCreate };
}

// ---------------------------------------------------------------------------------------------
// Building and switching releases
// ---------------------------------------------------------------------------------------------

/** git with the checkout marked safe, which root needs to read a checkout another account owns. */
export function git(sourceDir, args, options = {}) {
  return run('git', ['-c', `safe.directory=${sourceDir}`, '-C', sourceDir, ...args], {
    capture: true,
    label: `git ${args[0]}`,
    ...options,
  });
}

/** The commit a source tree is at, and whether it has uncommitted changes to tracked files. */
export function sourceState(sourceDir) {
  const head = git(sourceDir, ['rev-parse', 'HEAD'], { allowFail: true });
  if (head.status !== 0) return { commit: 'unknown', dirty: true };
  const status = git(sourceDir, ['status', '--porcelain', '--untracked-files=no'], { allowFail: true });
  return { commit: head.stdout.trim(), dirty: status.status !== 0 || status.stdout.trim() !== '' };
}

export function readBuildManifest(apiDir) {
  const file = path.join(apiDir, BUILD_MANIFEST);
  if (!fs.existsSync(file)) return undefined;
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return undefined;
  }
}

/** Whether the installed build is not the one the source tree would produce. */
export const needsBuild = (manifest, state, force) =>
  Boolean(force || !manifest || state.dirty || manifest.commit !== state.commit);

const nextOf = (dir) => `${dir}.next`;
const previousOf = (dir) => `${dir}.previous`;
const failedOf = (dir) => `${dir}.failed`;

/**
 * Publishes the API and builds the web application beside the running ones, as `api.next` and
 * `web.next`. Nothing that is serving is touched, so a build that fails leaves the installation
 * exactly as it was.
 */
export function buildRelease(layout) {
  const src = layout.sourceDir;
  const apiNext = nextOf(layout.apiDir);
  const webNext = nextOf(layout.webRoot);
  fs.rmSync(apiNext, { recursive: true, force: true });
  fs.rmSync(webNext, { recursive: true, force: true });
  fs.mkdirSync(layout.appDir, { recursive: true });

  const state = sourceState(src);
  say(`Publishing the API from ${src} (${state.commit.slice(0, 12)}${state.dirty ? ', with local changes' : ''})`);
  run(
    'dotnet',
    ['publish', path.join(src, 'server', 'src', 'SilexGis.Api'), '-c', 'Release', '-o', apiNext, '--nologo'],
    { cwd: src, env: { DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' }, label: 'dotnet publish' },
  );

  say('Building the web application');
  const clientDir = path.join(src, 'client');
  runNpm(['ci', '--no-audit', '--no-fund'], { cwd: clientDir });
  runNpm(['run', 'build'], { cwd: clientDir });
  fs.cpSync(path.join(clientDir, 'dist'), webNext, { recursive: true });

  const manifest = {
    commit: state.commit,
    dirty: state.dirty,
    builtAt: new Date().toISOString(),
    migrations: migrationIdsFromSource(src),
  };
  fs.writeFileSync(path.join(apiNext, BUILD_MANIFEST), `${JSON.stringify(manifest, null, 2)}\n`);
  return manifest;
}

/** Puts `next` in place of `dir`, keeping what was there as `previous`. */
function promote(dir) {
  if (!fs.existsSync(nextOf(dir))) fail(`${nextOf(dir)} is missing; nothing to switch to`);
  if (fs.existsSync(dir)) {
    fs.rmSync(previousOf(dir), { recursive: true, force: true });
    fs.renameSync(dir, previousOf(dir));
  }
  fs.renameSync(nextOf(dir), dir);
}

/** Puts `previous` back in place of `dir`, keeping the one that failed as `failed`. */
function demote(dir) {
  fs.rmSync(failedOf(dir), { recursive: true, force: true });
  if (fs.existsSync(dir)) fs.renameSync(dir, failedOf(dir));
  fs.renameSync(previousOf(dir), dir);
}

/**
 * Switches to the build `buildRelease` made, starts it, and judges it: the API's own readiness
 * first, then `verify` (the installation check). If it is not healthy and there was a build
 * before it, that build is put back and started again. The database is not rolled back — a
 * migration the new build applied stays applied — which is why an update backs up first and the
 * failure message names that backup.
 */
export async function activateRelease(layout, { verify, backupDir } = {}) {
  const hadPrevious = fs.existsSync(layout.apiDir);
  say('Switching to the new build');
  await stopApi(layout);
  promote(layout.apiDir);
  promote(layout.webRoot);
  if (layout.service === 'none') {
    note('service mode is "none": the build is in place; start the API yourself and run check.mjs');
    return { ok: true, started: false };
  }
  await startApi(layout);

  say(`Waiting for ${layout.apiUrl}/health/ready`);
  const health = await waitHealthy(layout, healthTimeout());
  let reason = health.ok ? undefined : health.reason;
  if (health.ok && verify) {
    const verdict = await verify();
    if (!verdict.ok) reason = `the installation check failed (${verdict.failed.join(', ')})`;
  }
  if (!reason) return { ok: true, started: true };

  console.error(`\n--- last lines of the API's log ---\n${apiLogTail(layout)}\n---`);
  if (!hadPrevious || !fs.existsSync(previousOf(layout.apiDir))) {
    fail(`the new build did not come up: ${reason}`);
  }
  say(`Rolling back to the previous build: ${reason}`);
  await stopApi(layout);
  demote(layout.apiDir);
  if (fs.existsSync(previousOf(layout.webRoot))) demote(layout.webRoot);
  await startApi(layout);
  const back = await waitHealthy(layout, healthTimeout());
  const restoreHint = backupDir
    ? `restore the backup at ${backupDir} with deploy/native/restore.mjs`
    : 'restore your latest backup with deploy/native/restore.mjs';
  if (back.ok) {
    fail(
      `the new build failed (${reason}); the previous build is running again. If the new build had already ` +
        `applied a database migration, restoring the code is not enough: ${restoreHint}.`,
    );
  }
  fail(`the new build failed (${reason}) AND the previous build did not come back up: ${restoreHint}.`);
}

/** Runs the API's `seed-demo` command against this installation, as the service account. */
export function seedDemo(layout) {
  const fileValues = parseEnvFile(fs.readFileSync(layout.envFile, 'utf8')).values;
  const ids = machineFacts().root ? accountIds(layout.serviceUser) : undefined;
  run(findExecutable('dotnet') ?? 'dotnet', [path.join(layout.apiDir, API_DLL), 'seed-demo'], {
    cwd: layout.apiDir,
    exactEnv: apiEnvironment(process.env, fileValues, layout),
    label: 'seed-demo',
    ...(ids ?? {}),
  });
}

// ---------------------------------------------------------------------------------------------
// Web server and service configuration
// ---------------------------------------------------------------------------------------------

/** Where the nginx site goes: the Debian layout when the host has it, else conf.d. */
export function defaultNginxTarget(exists = fs.existsSync) {
  if (exists('/etc/nginx/sites-available') && exists('/etc/nginx/sites-enabled')) {
    return { file: '/etc/nginx/sites-available/silexgis.conf', link: '/etc/nginx/sites-enabled/silexgis.conf' };
  }
  return { file: '/etc/nginx/conf.d/silexgis.conf' };
}

const nginxPidAlive = () => {
  try {
    return pidAlive(Number(fs.readFileSync('/run/nginx.pid', 'utf8').trim()));
  } catch {
    return false;
  }
};

/**
 * Renders the nginx site for this installation, keeps a reference copy in the configuration
 * directory, and — when nginx is this installation's web server — installs it, tests the whole
 * nginx configuration with it, and reloads (or starts) nginx. A rendering nginx rejects is taken
 * back out before anything is reloaded, so a running site is never replaced by a broken one.
 *
 * Returns what to record: the file and the hash of what was written there.
 */
export function convergeWebServer(layout, recorded) {
  const template = fs.readFileSync(path.join(layout.sourceDir, 'deploy', 'nginx', 'silexgis.conf'), 'utf8');
  const rendered = renderNginx(template, layout);
  fs.mkdirSync(layout.configDir, { recursive: true });
  fs.writeFileSync(path.join(layout.configDir, 'nginx-silexgis.conf'), rendered);
  if (layout.webServer !== 'nginx') {
    note(`web server "none": the rendered nginx site is at ${path.join(layout.configDir, 'nginx-silexgis.conf')}`);
    return {};
  }
  const target = layout.nginxConf ? { file: layout.nginxConf } : defaultNginxTarget();
  const nginx = findExecutable('nginx', ['/usr/sbin', '/usr/local/sbin']) ?? fail('nginx is not installed');
  const result = installConfigFile(target.file, rendered, recorded?.nginxConfHash);
  if (result.kept) warn(`${target.file} was changed by hand and is kept; this version's rendering is beside it as .new`);
  if (target.link && !fs.existsSync(target.link)) fs.symlinkSync(target.file, target.link);

  const test = run(nginx, ['-t'], { capture: true, allowFail: true });
  if (test.status !== 0) {
    if (result.changed) {
      if (result.previous === undefined) fs.rmSync(target.file, { force: true });
      else fs.writeFileSync(target.file, result.previous);
    }
    fail(`nginx rejects its configuration with this site in it; the site was taken back out:\n${test.stderr.trim()}`);
  }
  if (machineFacts().systemd) {
    run('systemctl', ['enable', '--quiet', 'nginx'], { allowFail: true });
    run('systemctl', ['reload-or-restart', 'nginx']);
  } else if (nginxPidAlive()) {
    run(nginx, ['-s', 'reload']);
  } else {
    run(nginx, []);
  }
  note(`nginx site ${result.kept ? 'kept as edited' : result.changed ? 'written' : 'unchanged'}: ${target.file}`);
  return { nginxConf: target.file, nginxConfHash: result.hash };
}

/** Installs and enables the API's systemd unit. Returns what to record. */
export function convergeUnit(layout, recorded, dotnetPath) {
  if (layout.service !== 'systemd') return {};
  const template = fs.readFileSync(path.join(layout.sourceDir, 'deploy', 'systemd', 'silexgis-api.service'), 'utf8');
  const file = `/etc/systemd/system/${layout.serviceName}.service`;
  const result = installConfigFile(file, renderUnit(template, layout, dotnetPath), recorded?.unitHash);
  if (result.kept) warn(`${file} was changed by hand and is kept; this version's rendering is beside it as .new`);
  run('systemctl', ['daemon-reload']);
  run('systemctl', ['enable', '--quiet', layout.serviceName]);
  note(`systemd unit ${result.kept ? 'kept as edited' : result.changed ? 'written' : 'unchanged'}: ${file}`);
  return { unitHash: result.hash };
}

/** Installs the nightly backup timer (systemd only). Returns what to record. */
export function convergeBackupTimer(layout, recorded, keep) {
  if (layout.service !== 'systemd') return {};
  const { service, timer } = renderBackupUnits(layout, process.execPath, keep);
  const serviceFile = '/etc/systemd/system/silexgis-backup.service';
  const timerFile = '/etc/systemd/system/silexgis-backup.timer';
  const a = installConfigFile(serviceFile, service, recorded?.backupServiceHash);
  const b = installConfigFile(timerFile, timer, recorded?.backupTimerHash);
  run('systemctl', ['daemon-reload']);
  run('systemctl', ['enable', '--now', '--quiet', 'silexgis-backup.timer']);
  note(`nightly backup timer enabled (keeps ${keep}; into ${layout.backupDir})`);
  return { backupServiceHash: a.hash, backupTimerHash: b.hash };
}

/** Writes the install record, merging what an earlier run recorded. */
export function writeRecord(layout, recorded, extra) {
  const record = { ...(recorded ?? {}), ...recordOf(layout), ...extra, recordedAt: new Date().toISOString() };
  fs.mkdirSync(layout.configDir, { recursive: true });
  fs.writeFileSync(layout.recordFile, `${JSON.stringify(record, null, 2)}\n`);
  return record;
}

/**
 * One updater (or installer) at a time, across processes. Two concurrent builds on a small host
 * is how an update runs it out of memory and leaves neither version running. A lock whose
 * process is gone is taken over rather than left to block every later run.
 */
export function acquireLock(file) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const fd = fs.openSync(file, 'wx');
      fs.writeSync(fd, `${process.pid}\n`);
      fs.closeSync(fd);
      return () => fs.rmSync(file, { force: true });
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
      const holder = Number(fs.readFileSync(file, 'utf8').trim());
      if (Number.isInteger(holder) && holder > 0 && pidAlive(holder)) {
        fail(`another install or update is running (pid ${holder}); lock ${file}`);
      }
      fs.rmSync(file, { force: true });
    }
  }
  fail(`could not take the lock ${file}`);
}

/** Starts a long streaming child and resolves with its exit code and collected standard error. */
export function spawnStreaming(command, args, { env, cwd, stdout = 'ignore' } = {}) {
  const child = spawn(command, args, { env, cwd, stdio: ['pipe', stdout, 'pipe'], windowsHide: true });
  const stderr = [];
  child.stderr.on('data', (chunk) => stderr.push(chunk));
  const done = new Promise((resolve, reject) => {
    child.on('error', reject);
    child.on('close', (code) => resolve({ code, stderr: Buffer.concat(stderr).toString('utf8') }));
  });
  return { child, done };
}
