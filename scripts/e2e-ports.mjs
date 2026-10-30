// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Whether the three ports a browser run needs are free to take, and what to say when they are not.
//
// The run stands up a database, an API and a development server, each on a port of its own. If one
// of them is already listening, the failure that follows is never "port in use": a database
// container that cannot publish its port says it could not start; a development server that finds
// its port taken is told by the Playwright configuration not to adopt it and dies at a timeout; and
// an API port taken by something that is not an API is read as a running API. Worst is the quiet
// case, a listener that answers on the API port and is taken for the stack the run just built — the
// suite then reports on somebody else's server. So the question is asked up front, of all three
// ports, with a plain TCP connection rather than an HTTP request: a database or a half-started
// server does not speak HTTP and would otherwise count as free.

import net from 'node:net';

/** The exit code of a run that refuses to start. Distinct from a failed suite (1) and from an
 * --affected selection that is wider than the map (10). */
export const REFUSED_EXIT_CODE = 11;

/** What each port is for and which variable moves it. */
export const PORT_ROLES = [
  { key: 'db', label: 'database', variable: 'SILEXGIS_E2E_DB_PORT' },
  { key: 'api', label: 'API', variable: 'SILEXGIS_E2E_API_PORT' },
  { key: 'dev', label: 'development server', variable: 'SILEXGIS_E2E_DEV_PORT' },
];

/**
 * True when something accepts a connection on `port` on either loopback address. Only an accepted
 * connection counts: a refusal means free, and so does silence. On Windows a refused loopback
 * connect takes about two seconds (the stack sends the SYN again after the reset), so the wait is
 * set well above that and running out of it is read as "nobody answered", never as a listener. An
 * address family the machine does not have is not a listener either.
 */
export function isListening(port, { hosts = ['127.0.0.1', '::1'], timeoutMs = 5000 } = {}) {
  const probe = (host) => new Promise((resolve) => {
    const socket = net.connect({ port, host });
    let done = false;
    const finish = (value) => {
      if (done) return;
      done = true;
      socket.destroy();
      resolve(value);
    };
    socket.setTimeout(timeoutMs, () => finish(false));
    socket.once('connect', () => finish(true));
    socket.once('error', () => finish(false));
  });
  return Promise.all(hosts.map(probe)).then((answers) => answers.some(Boolean));
}

/**
 * Why the run cannot start, or null when it can.
 *
 * `ports` is `{ db, api, dev }` as the strings the environment gave. The refusal carries a stable
 * code at the front of its message and names every offending port with the variable that moves it,
 * so that the person reading it, or a log search, needs nothing else. `probed` names the roles
 * whose ports are asked about; every port is still checked for being a number and distinct.
 */
export async function portRefusal(ports, probe = isListening, probed = PORT_ROLES.map((role) => role.key)) {
  const problems = [];
  const numbers = new Map();
  for (const role of PORT_ROLES) {
    const raw = String(ports[role.key]);
    const port = Number(raw);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      problems.push({
        code: 'E2E_PORT_INVALID',
        text: `${role.variable} is "${raw}", which is not a port number`,
      });
    } else {
      numbers.set(role, port);
    }
  }
  if (problems.length > 0) return render(problems);

  const seen = new Map();
  for (const [role, port] of numbers) {
    if (seen.has(port)) {
      problems.push({
        code: 'E2E_PORTS_NOT_DISTINCT',
        text: `the ${seen.get(port).label} and the ${role.label} are both set to port ${port}; `
          + `set ${seen.get(port).variable} and ${role.variable} to different ports`,
      });
    } else {
      seen.set(port, role);
    }
  }
  if (problems.length > 0) return render(problems);

  const listening = await Promise.all(
    [...numbers].filter(([role]) => probed.includes(role.key)).map(async ([role, port]) => ((await probe(port)) ? { role, port } : null)));
  for (const found of listening.filter(Boolean)) {
    problems.push({
      code: 'E2E_PORT_IN_USE',
      text: `port ${found.port} (the ${found.role.label}) is already listening, and not from this run. `
        + `Stop what is using it, or set ${found.role.variable} to a free port, or set `
        + 'SILEXGIS_E2E_REUSE=1 to run against a stack that is already up on purpose',
    });
  }
  return problems.length > 0 ? render(problems) : null;
}

function render(problems) {
  return {
    code: problems[0].code,
    message: problems.map((problem) => `${problem.code}: ${problem.text}.`).join('\n'),
  };
}
