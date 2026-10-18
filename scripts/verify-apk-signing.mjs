import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export function verifyApkSigning(report, expectedPin) {
  const pin = expectedPin.replace(/[\s:]/g, '').toLowerCase();
  if (!/^[a-f0-9]{64}$/.test(pin)) throw new Error('Invalid pinned Android certificate fingerprint');
  if (!/^Verifies\r?$/m.test(report) || !/^Number of signers: 1\r?$/m.test(report)) {
    throw new Error('Expected a verified APK with exactly one signer');
  }
  // Build tools 36.1 use "V2 Signer:"; older versions use "Signer #1".
  // Match certificate digests only, never the different public-key digest.
  const digests = [...report.matchAll(/^(?:Signer #\d+|V\d+(?:\.\d+)? Signer):? certificate SHA-256 digest: ([a-f0-9]{64})\s*$/gim)]
    .map(match => match[1].toLowerCase());
  if (!digests.length || digests.some(digest => digest !== pin)) {
    throw new Error('The APK signing certificate differs from the pinned release identity');
  }
  return { component: 'android', signed: true, certificateSha256: pin };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [, , reportPath, pinPath] = process.argv;
  if (!reportPath || !pinPath) throw new Error('Usage: verify-apk-signing.mjs REPORT PIN');
  process.stdout.write(JSON.stringify(verifyApkSigning(readFileSync(reportPath, 'utf8'), readFileSync(pinPath, 'utf8'))) + '\n');
}
