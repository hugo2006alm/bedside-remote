import { test } from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import { runInNewContext } from 'node:vm';

test('Edge keeps callback channels open for asynchronous player and popup responses', async () => {
  const bundle = await build({ entryPoints: ['src/messages.ts'], bundle: true, write: false, format: 'iife', globalName: 'bridge', define: { BEDSIDE_EDGE: 'true', browser: 'chrome' } });
  let listener: (message: unknown, sender: object, respond: (value: unknown) => void) => unknown;
  const context = { chrome: { runtime: { onMessage: { addListener: (value: typeof listener) => { listener = value; } } } } };
  const vm = runInNewContext(`${bundle.outputFiles[0]!.text}; bridge`, context) as { listen: (handler: (message: unknown) => Promise<unknown> | undefined) => void };
  vm.listen(message => message === 'known' ? Promise.resolve({ ok: true }) : message === 'fail' ? Promise.reject(new Error('fixture')) : undefined);
  const responses: unknown[] = [];
  assert.equal(listener!('known', {}, value => responses.push(value)), true);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(JSON.stringify(responses), '[{"ok":true}]');
  assert.equal(listener!('unknown', {}, () => assert.fail('Unknown message claimed a response')), undefined);
  assert.equal(listener!('fail', {}, value => responses.push(value)), true);
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal((responses.at(-1) as { ok: boolean }).ok, false);
});
