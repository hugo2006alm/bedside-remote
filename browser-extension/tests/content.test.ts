import assert from 'node:assert/strict';
import { test } from 'node:test';
import { JSDOM } from 'jsdom';
import { build } from 'esbuild';

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
