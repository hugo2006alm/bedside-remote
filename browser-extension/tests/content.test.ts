import assert from 'node:assert/strict';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import { build } from 'esbuild';

for (const failure of ['sync', 'async', 'pagehide'] as const) {
  test(`invalidated extension stops safely after ${failure} messaging failure`, async () => {
    const dom = new JSDOM('<title>Netflix</title><video></video>', { url: 'https://www.netflix.com/watch/12345', runScripts: 'outside-only' });
    const timers = new Map<number, () => void>();
    const errors: unknown[] = [];
    let sent = 0;
    dom.window.addEventListener('error', event => { errors.push(event.error); });
    Object.assign(dom.window, {
      browser: { runtime: {
        sendMessage: (message: { type: string }) => {
          sent++;
          if (failure === 'pagehide' && message.type !== 'player.gone') return Promise.resolve({ ok: true });
          const error = new dom.window.Error('Extension context invalidated.');
          if (failure === 'async') return Promise.reject(error);
          throw error;
        },
        onMessage: { addListener: () => {} }
      } },
      setInterval: (callback: () => void) => { timers.set(1, callback); return 1; },
      clearInterval: (id: number) => { timers.delete(id); }
    });
    const bundle = await build({ entryPoints: ['src/content.ts'], bundle: true, write: false, format: 'iife', target: 'es2022' });
    dom.window.eval(bundle.outputFiles[0]!.text);
    await new Promise(resolve => setImmediate(resolve));
    dom.window.dispatchEvent(new dom.window.PageTransitionEvent('pagehide'));
    const before = sent;
    dom.window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true }));
    dom.window.document.querySelector('video')!.dispatchEvent(new dom.window.Event('playing'));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(timers.size, 0, 'dead extension contexts cannot restart polling on bfcache restoration');
    assert.equal(sent, before, 'detached media listeners cannot message the invalidated extension');
    assert.deepEqual(errors, [], 'synchronous pagehide messaging errors are caught');
    dom.window.close();
  });
}

test('rejected snapshots retry unchanged media and current-document identity follows SPA routes', async () => {
  const dom = new JSDOM('<title>Netflix</title><video></video>', { url: 'https://www.netflix.com/browse', runScripts: 'outside-only' });
  const listeners: ((value: unknown) => unknown)[] = [];
  const timers: (() => void)[] = [];
  let reports = 0;
  let now = 1000;
  dom.window.Date.now = () => now;
  Object.assign(dom.window, {
    browser: { runtime: {
      sendMessage: async (value: { type: string }) => value.type === 'player.state' ? { ok: ++reports > 1 } : undefined,
      onMessage: { addListener: (listener: (value: unknown) => unknown) => listeners.push(listener) }
    } },
    setInterval: (callback: () => void) => { timers.push(callback); return 1; }, clearInterval: () => {}
  });
  const bundle = await build({ entryPoints: ['src/content.ts'], bundle: true, write: false, format: 'iife', target: 'es2022' });
  dom.window.eval(bundle.outputFiles[0]!.text);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(reports, 1);
  now += 1000; timers[0]!();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(reports, 2, 'a rejected initial snapshot retries even without a new media state');
  now += 1000; timers[0]!();
  assert.equal(reports, 2, 'an accepted unchanged snapshot is deduplicated');
  dom.reconfigure({ url: 'https://www.netflix.com/watch/12345' });
  const identity = await listeners[0]!({ type: 'player.identity' }) as { instance: string; url: string };
  assert.equal(identity.url, 'https://www.netflix.com/watch/12345');
  assert.equal(typeof identity.instance, 'string');
  dom.window.close();
});

test('content never manufactures natural completion for load, SPA navigation or untrusted events', async () => {
  const dom = new JSDOM('<!doctype html><title>Fixture</title><div id="movie_player"><video></video></div>', {
    url: 'https://www.youtube.com/watch?v=dQw4w9WgXcQ', runScripts: 'outside-only'
  });
  const out: { type: string; state?: { ended: boolean; videoId?: string } }[] = [];
  const timers = new Map<number, () => void>();
  let counter = 0;
  Object.assign(dom.window, {
    browser: { runtime: { sendMessage: async (value: { type: string }) => { out.push(value); }, onMessage: { addListener: () => {} } } },
    setInterval: (callback: () => void) => { const id = ++counter; timers.set(id, callback); return id; },
    clearInterval: (id: number) => { timers.delete(id); }
  });
  const video = dom.window.document.querySelector('video')!;
  Object.defineProperty(video, 'ended', { value: true });
  const bundle = await build({ entryPoints: ['src/content.ts'], bundle: true, write: false, format: 'iife', target: 'es2022' });
  dom.window.eval(bundle.outputFiles[0]!.text);
  assert.equal(out.at(-1)?.state?.ended, false, 'an already ended element on load is not a natural transition');
  video.dispatchEvent(new dom.window.Event('ended'));
  assert.equal(out.at(-1)?.state?.ended, false, 'untrusted synthetic ended events are rejected');
  dom.reconfigure({ url: 'https://www.youtube.com/watch?v=aaaaaaaaaaa' });
  for (const callback of timers.values()) callback();
  // Force the periodic sampler beyond its rate limit without a real wait.
  dom.window.dispatchEvent(new dom.window.PageTransitionEvent('pagehide'));
  assert.equal(out.at(-1)?.type, 'player.gone');
  assert.equal(timers.size, 0);
  dom.window.dispatchEvent(new dom.window.PageTransitionEvent('pageshow', { persisted: true }));
  assert.equal(out.at(-1)?.state?.videoId, 'aaaaaaaaaaa');
  assert.equal(out.at(-1)?.state?.ended, false, 'navigation and bfcache restoration clear completion');
  assert.equal(timers.size, 1);
  dom.window.dispatchEvent(new dom.window.PageTransitionEvent('pagehide'));
  assert.equal(timers.size, 0, 'restored pages clean up timers again');
  dom.window.close();
});
