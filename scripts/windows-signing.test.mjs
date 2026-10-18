import { test } from 'node:test';
import assert from 'node:assert/strict';
import { signingProvider } from './windows-signing.mjs';

const approved = { SIGN_RELEASE: 'true', WINDOWS_SIGNING_PROVIDER: 'signpath', SIGNPATH_API_TOKEN: 'fixture', SIGNPATH_ORGANIZATION_ID: 'fixture', SIGNPATH_PROJECT_SLUG: 'fixture', SIGNPATH_SIGNING_POLICY_SLUG: 'release-signing', SIGNPATH_CERTIFICATE_THUMBPRINT: 'AB'.repeat(20) };
test('PR and non-release builds never request signing', () => {
  assert.equal(signingProvider({ ...approved, GITHUB_EVENT_NAME: 'pull_request' }), 'none');
  assert.equal(signingProvider({ ...approved, SIGN_RELEASE: 'false' }), 'none');
});
test('SignPath requires complete configuration and cannot silently publish unsigned', () => {
  assert.equal(signingProvider(approved), 'signpath');
  for (const key of Object.keys(approved).filter(key => key.startsWith('SIGNPATH_'))) assert.throws(() => signingProvider({ ...approved, [key]: '' }));
  assert.throws(() => signingProvider({ ...approved, SIGNPATH_CERTIFICATE_THUMBPRINT: 'bad' }));
});
test('PFX remains supported and partial credentials fail closed', () => {
  assert.equal(signingProvider({ SIGN_RELEASE: 'true' }), 'none');
  assert.equal(signingProvider({ SIGN_RELEASE: 'true', PFX_BASE64: 'fixture', PFX_PASSWORD: 'fixture' }), 'pfx');
  assert.throws(() => signingProvider({ SIGN_RELEASE: 'true', PFX_BASE64: 'fixture' }));
  assert.throws(() => signingProvider({ SIGN_RELEASE: 'true', WINDOWS_SIGNING_PROVIDER: 'unknown' }));
});
