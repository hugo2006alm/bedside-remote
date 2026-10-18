import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyApkSigning } from './verify-apk-signing.mjs';

const pin = 'ab'.repeat(32);
const header = 'Verifies\nNumber of signers: 1\n';

test('accepts current apksigner V2 output and repeated matching signature schemes', () => {
  const report = header + `V2 Signer: certificate SHA-256 digest: ${pin}\nV3.1 Signer: certificate SHA-256 digest: ${pin}\n`;
  assert.equal(verifyApkSigning(report, pin).certificateSha256, pin);
});

test('accepts older apksigner numbered output, CRLF and formatted uppercase pins', () => {
  const report = (header + `Signer #1 certificate SHA-256 digest: ${pin.toUpperCase()}\n`).replaceAll('\n', '\r\n');
  assert.equal(verifyApkSigning(report, pin.toUpperCase().match(/../g).join(':')).signed, true);
});

test('rejects another certificate even when a matching digest is also present', () => {
  assert.throws(() => verifyApkSigning(header + `V2 Signer: certificate SHA-256 digest: ${pin}\nV3 Signer: certificate SHA-256 digest: ${'cd'.repeat(32)}\n`, pin));
});

test('does not mistake a public-key fingerprint or missing report for the certificate', () => {
  assert.throws(() => verifyApkSigning(header + `V2 Signer: public key SHA-256 digest: ${pin}\n`, pin));
  assert.throws(() => verifyApkSigning('', pin));
  assert.throws(() => verifyApkSigning(header, 'invalid'));
});

test('rejects failed verification and multiple signers', () => {
  const certificate = `Signer #1 certificate SHA-256 digest: ${pin}\n`;
  assert.throws(() => verifyApkSigning('DOES NOT VERIFY\nNumber of signers: 1\n' + certificate, pin));
  assert.throws(() => verifyApkSigning('Verifies\nNumber of signers: 2\n' + certificate, pin));
});
