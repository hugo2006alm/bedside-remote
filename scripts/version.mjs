import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const pattern = /^v?(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta)\.(\d+))?$/;

export function parseVersion(value) {
  const match = pattern.exec(value);
  if (!match) return null;
  return { major: +match[1], minor: +match[2], patch: +match[3], channel: match[4] ?? 'stable', serial: +(match[5] ?? 0), tag: value };
}

export function compareVersions(a, b) {
  for (const part of ['major', 'minor', 'patch']) {
    if (a[part] !== b[part]) return a[part] - b[part];
  }
  const order = { alpha: 0, beta: 1, stable: 2 };
  return order[a.channel] - order[b.channel] || a.serial - b.serial;
}

export function calculateRelease(tags, messages, channel = 'alpha', force = false) {
  if (!['alpha', 'beta', 'stable'].includes(channel)) throw new Error('Invalid release channel');
  const versions = tags.map(parseVersion).filter(Boolean).sort(compareVersions);
  const latest = versions.at(-1);
  let level = 0;
  for (const message of messages) {
    const header = message.split('\n')[0];
    if (/^[a-z]+(?:\([^\n)]*\))?!:/.test(header) || /^BREAKING[ -]CHANGE:\s/m.test(message)) level = 3;
    else if (/^feat(?:\([^\n)]*\))?:/.test(header)) level = Math.max(level, 2);
    else if (/^(fix|perf)(?:\([^\n)]*\))?:/.test(header)) level = Math.max(level, 1);
  }
  if (!level && !force) return { publish: false, version: latest?.tag.replace(/^v/, '') ?? '0.1.0-alpha.1', previous: latest?.tag ?? '' };
  const next = { major: latest?.major ?? 0, minor: latest?.minor ?? 0, patch: latest?.patch ?? 0 };
  if (!latest) Object.assign(next, { major: 0, minor: 1, patch: 0 });
  else if (level === 3) { next.major++; next.minor = 0; next.patch = 0; }
  else if (level === 2) { next.minor++; next.patch = 0; }
  else next.patch++;
  const core = `${next.major}.${next.minor}.${next.patch}`;
  const version = channel === 'stable' ? core : `${core}-${channel}.1`;
  return { publish: true, version, previous: latest?.tag ?? '' };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8' }).trim();
  const tags = git('tag', '--merged', 'HEAD', '--list', 'v*').split('\n').filter(Boolean);
  const latest = tags.map(parseVersion).filter(Boolean).sort(compareVersions).at(-1);
  const range = latest ? `${latest.tag}..HEAD` : 'HEAD';
  const messages = git('log', range, '--format=%s%n%b%x00').split('\0').map(s => s.trim()).filter(Boolean);
  const result = calculateRelease(tags, messages, process.env.RELEASE_CHANNEL || 'alpha', process.env.FORCE_RELEASE === 'true');
  const output = Object.entries(result).map(([key, value]) => `${key}=${value}`).join('\n') + '\n';
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, output);
  process.stdout.write(output);
}
