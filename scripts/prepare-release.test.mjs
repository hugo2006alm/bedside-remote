import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { validateRelease, writeChecksums } from './prepare-release.mjs';

function fixture(version, signed = false) {
  const directory = mkdtempSync(join(tmpdir(), 'bedside-release-'));
  for (const component of ['android', 'windows', 'extension', 'edge']) writeFileSync(join(directory, `${component}-signing.json`), JSON.stringify({ component, signed }));
  for (const file of [
    `bedside-remote-android-v${version}${signed ? '' : '-development'}.apk`,
    `bedside-remote-windows-x64-v${version}-setup.exe`,
    `bedside-remote-extension-v${version}${signed ? '.xpi' : '-development.zip'}`,
    `bedside-remote-extension-source-v${version}.zip`,
    `bedside-remote-edge-v${version}${signed ? '.crx' : '-development.zip'}`,
  ]) writeFileSync(join(directory, file), 'nonempty test artifact');
  return directory;
}

test('incomplete signed/development distribution is never promoted to stable', () => {
  const dir = fixture('0.1.0');
  try { assert.throws(() => validateRelease(dir, '0.1.0', true), /Stable release/); }
  finally { rmSync(dir, { recursive: true }); }
});
test('physical validation remains mandatory even when all signatures are configured', () => {
  const dir = fixture('1.0.0', true);
  try {
    assert.throws(() => validateRelease(dir, '1.0.0', false), /Stable release/);
    assert.equal(validateRelease(dir, '1.0.0', true).complete, true);
  } finally { rmSync(dir, { recursive: true }); }
});
test('alpha correctly identifies missing signatures and missing payloads fail validation', () => {
  const dir = fixture('0.1.0-alpha.1');
  try {
    assert.equal(validateRelease(dir, '0.1.0-alpha.1', false).complete, false);
    rmSync(join(dir, 'bedside-remote-android-v0.1.0-alpha.1-development.apk'));
    assert.throws(() => validateRelease(dir, '0.1.0-alpha.1', false), /Missing or empty artifact/);
  } finally { rmSync(dir, { recursive: true }); }
});
test('checksums include manifest and are reproducible', () => {
  const dir = mkdtempSync(join(tmpdir(), 'bedside-checksum-'));
  try {
    writeFileSync(join(dir, 'payload.apk'), 'payload');
    writeChecksums(dir);
    const output = readFileSync(join(dir, 'SHA256SUMS.txt'), 'utf8');
    assert.equal(output, `${createHash('sha256').update('payload').digest('hex')}  payload.apk\n`);
    writeChecksums(dir);
    assert.equal(readFileSync(join(dir, 'SHA256SUMS.txt'), 'utf8'), output);
  } finally { rmSync(dir, { recursive: true }); }
});

test('unpacked Edge distribution cannot be hidden behind successful Mozilla signing', () => {
  const dir = fixture('1.0.0', true);
  try {
    writeFileSync(join(dir, 'edge-signing.json'), JSON.stringify({ component: 'edge', signed: false }));
    writeFileSync(join(dir, 'bedside-remote-edge-v1.0.0-development.zip'), 'fixture');
    assert.throws(() => validateRelease(dir, '1.0.0', true), /Stable release/);
  } finally { rmSync(dir, { recursive: true }); }
});
