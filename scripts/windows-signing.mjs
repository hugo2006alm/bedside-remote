import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

export function signingProvider(env) {
  if (env.SIGN_RELEASE !== 'true' || env.GITHUB_EVENT_NAME === 'pull_request') return 'none';
  const provider = env.WINDOWS_SIGNING_PROVIDER || 'auto';
  if (!['auto', 'none', 'pfx', 'signpath'].includes(provider)) throw new Error('Unknown Windows signing provider.');
  if (provider === 'none') return 'none';
  if (provider === 'signpath') {
    for (const key of ['SIGNPATH_API_TOKEN', 'SIGNPATH_ORGANIZATION_ID', 'SIGNPATH_PROJECT_SLUG', 'SIGNPATH_SIGNING_POLICY_SLUG', 'SIGNPATH_CERTIFICATE_THUMBPRINT']) {
      if (!env[key]?.trim()) throw new Error(`Missing required SignPath configuration: ${key}`);
    }
    if (!/^[a-f0-9]{40}$/i.test(env.SIGNPATH_CERTIFICATE_THUMBPRINT)) throw new Error('Invalid SignPath certificate thumbprint.');
    return 'signpath';
  }
  if (env.PFX_BASE64 && env.PFX_PASSWORD) return 'pfx';
  if (provider === 'pfx' || env.PFX_BASE64 || env.PFX_PASSWORD) throw new Error('Incomplete PFX signing configuration.');
  return 'none';
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const provider = signingProvider(process.env);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `provider=${provider}\n`);
  console.log(`Windows signing provider: ${provider}`);
}
