import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseVersion } from './version.mjs';

export function validateRelease(directory, version, physicallyValidated) {
  const parsed = parseVersion(version);
  if (!parsed || version.startsWith('v')) throw new Error('Invalid application version');
  const signing = {};
  for (const component of ['android', 'windows', 'extension', 'edge']) {
    const report = JSON.parse(readFileSync(join(directory, `${component}-signing.json`), 'utf8').replace(/^\uFEFF/, ''));
    if (report.component !== component || typeof report.signed !== 'boolean') throw new Error(`Invalid signing report: ${component}`);
    signing[component] = report;
  }
  const expected = [
    `bedside-remote-android-v${version}${signing.android.signed ? '' : '-development'}.apk`,
    `bedside-remote-windows-x64-v${version}-setup.exe`,
    `bedside-remote-extension-v${version}${signing.extension.signed ? '.xpi' : '-development.zip'}`,
    `bedside-remote-extension-source-v${version}.zip`,
    `bedside-remote-edge-v${version}${signing.edge.signed ? '.crx' : '-development.zip'}`,
  ];
  for (const file of expected) {
    if (!statSync(join(directory, file), { throwIfNoEntry: false })?.size) throw new Error(`Missing or empty artifact: ${file}`);
  }
  const complete = Object.values(signing).every(report => report.signed) && physicallyValidated;
  if (parsed.channel === 'stable' && !complete) throw new Error('Stable release requires all distribution signatures and recorded physical validation');
  return { version, protocolVersion: 1, channel: parsed.channel, complete, physicallyValidated, signing, artifacts: expected };
}

export function writeChecksums(directory) {
  const names = readdirSync(directory).filter(name => name !== 'SHA256SUMS.txt').sort();
  const lines = names.map(name => `${createHash('sha256').update(readFileSync(join(directory, name))).digest('hex')}  ${name}`);
  writeFileSync(join(directory, 'SHA256SUMS.txt'), lines.join('\n') + '\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const version = process.env.APP_VERSION;
  const manifest = validateRelease('artifacts', version, process.env.PHYSICAL_VALIDATION_APPROVED === 'true');
  manifest.commit = process.env.GITHUB_SHA ?? execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  writeFileSync('artifacts/release-manifest.json', JSON.stringify(manifest, null, 2) + '\n');
  writeChecksums('artifacts');
  const range = process.env.PREVIOUS_TAG ? `${process.env.PREVIOUS_TAG}..HEAD` : 'HEAD';
  const history = execFileSync('git', ['log', range, '--format=- %s (%h)'], { encoding: 'utf8' }).trim();
  const limits = Object.values(manifest.signing).filter(report => !report.signed).map(report => `- ${report.component}: ${report.reason || 'Distribution signing is not configured.'}`);
  if (!manifest.physicallyValidated) limits.push('- Physical Android/Windows and real browser streaming validation is pending. Automated checks do not establish DRM or autoplay compatibility.');
  const notes = `# Bedside Remote v${version}\n\n${manifest.complete ? 'Complete distribution.' : '**Incomplete development prerelease.**'} Protocol v1 across all components.\n\n${history}\n\n## Installation\n\nSee [README](https://github.com/hugo2006alm/bedside-remote/blob/${manifest.commit}/README.md) for setup and pairing. Updates preserve Windows per-user configuration; keep the same Android distribution key to preserve app data.\n\n## Validation and signing\n\n${limits.length ? limits.join('\n') : 'Distribution signatures and recorded physical validation are complete.'}\n\nDevelopment APKs use a debug key. The development extension ZIP is for temporary loading, not normal signed installation. Unsigned Windows installers may trigger Windows reputation warnings. Verify downloads with SHA256SUMS.txt.\n`;
  writeFileSync('release-notes.md', notes + `\n## Code signing policy\n\nCopyright (c) 2026 hugo2006alm; project source is MIT-licensed. See the [Code signing policy](https://github.com/hugo2006alm/bedside-remote/blob/main/docs/code-signing.md) and [Privacy policy](https://github.com/hugo2006alm/bedside-remote/blob/main/docs/privacy.md). Free SignPath Foundation signing is being requested; it is not active until the provider approves and CI verifies production signatures.\n`);
}
