import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';
import { build } from 'esbuild';

class EventBus {
  listeners: ((...args: unknown[]) => unknown)[] = [];
  addListener = (listener: (...args: unknown[]) => unknown) => { this.listeners.push(listener); };
  emit(...args: unknown[]) { return this.listeners.map(listener => listener(...args)); }
}
test('background tracks multiple tabs, explicit selection, reload and native command results', async () => {
  const runtimeMessage = new EventBus();
  const nativeMessage = new EventBus();
  const nativeDisconnect = new EventBus();
  const activated = new EventBus();
  const updated = new EventBus();
  const removed = new EventBus();
  const nativeOut: Record<string, unknown>[] = [];
  const playerCommands: { tabId: number; message: unknown }[] = [];
  let connections = 0;
  let nativeError: { message: string } | undefined;
  const url = 'https://www.youtube.com/watch?v=dQw4w9WgXcQ';
  const live = new Map([[1, { id: 1, url, active: true }], [2, { id: 2, url: 'https://www.netflix.com/watch/123', active: false }]]);
  const timers = new Set<ReturnType<typeof setTimeout>>();
  const bundle = await build({ entryPoints: ['src/background.ts'], bundle: true, write: false, format: 'iife', target: 'es2022' });
  const source = bundle.outputFiles[0]!.text;
  const api = {
    runtime: {
      id: 'bedside-remote@hugo2006alm', onMessage: runtimeMessage,
      getURL: (path: string) => `moz-extension://fixture/${path}`,
      getManifest: () => ({ host_permissions: ['https://www.youtube.com/*'] }),
      connectNative: () => { connections++; return { get error() { return nativeError; }, onMessage: nativeMessage, onDisconnect: nativeDisconnect, postMessage: (message: Record<string, unknown>) => nativeOut.push(message), disconnect: () => {} }; }
    },
    tabs: {
      get: async (id: number) => live.get(id), query: async () => [],
      onActivated: activated, onUpdated: updated, onRemoved: removed,
      sendMessage: async (tabId: number, message: unknown) => { playerCommands.push({ tabId, message }); return { ok: true }; }
    },
    alarms: { onAlarm: new EventBus(), create: async () => {} }
  };
  runInNewContext(source, {
    browser: api, TextEncoder, URL, console,
    setTimeout: (callback: () => void, delay: number) => {
      const timer = setTimeout(() => { timers.delete(timer); callback(); }, delay);
      timers.add(timer); return timer;
    },
    clearTimeout: (timer: ReturnType<typeof setTimeout>) => { timers.delete(timer); clearTimeout(timer); }
  });
  const state = (platform: string) => ({ platform, title: 'Video', paused: false, position: 5, duration: 100, ended: false, capabilities: ['playPause', 'seek'], ...(platform === 'youtube' ? { videoId: 'dQw4w9WgXcQ' } : {}) });
  async function report(tabId: number, instance: string, value: unknown) {
    await Promise.all(runtimeMessage.emit({ type: 'player.state', instance, state: value }, { id: api.runtime.id, tab: live.get(tabId), frameId: 0, url: live.get(tabId)!.url }));
  }
  try {
    await report(1, 'first', state('youtube'));
    activated.emit({ tabId: 2 });
    await report(2, 'second', state('netflix'));
    assert.equal(nativeOut.at(-1)!.activeTabId, 2, 'newly activated compatible tab becomes target after its first player snapshot');
    assert.equal((nativeOut.at(-1)!.tabs as unknown[]).length, 2);
    nativeMessage.emit({ version: 1, type: 'browser.command', id: 'select', command: 'selectTab', tabId: 1 });
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(nativeOut.at(-2)!.activeTabId, 1, 'native selection synchronizes popup target without player actions');
    assert.equal(nativeOut.at(-1)!.ok, true);
    assert.equal(playerCommands.length, 0);
    nativeMessage.emit({ version: 1, type: 'browser.command', id: 'select-missing', command: 'selectTab', tabId: 999 });
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(nativeOut.at(-1)!.ok, false, 'selection rejects a tab absent from compatible player state');
    await Promise.all(runtimeMessage.emit({ type: 'popup.select', tabId: 2 }, { id: api.runtime.id, url: api.runtime.getURL('popup.html') }));
    await report(1, 'first', { ...state('youtube'), position: 10 });
    const popup = await runtimeMessage.emit({ type: 'popup.status' }, { id: api.runtime.id, url: api.runtime.getURL('popup.html') })[0] as { activeTabId: number };
    assert.equal(popup.activeTabId, 2, 'active background tab progress must not override explicit selection');
    nativeMessage.emit({ version: 1, type: 'browser.command', id: 'seek', command: 'seek', position: 30 });
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(playerCommands.at(-1)?.tabId, 2);
    assert.equal(nativeOut.at(-1)!.type, 'browser.result');
    assert.equal(nativeOut.at(-1)!.ok, true);
    updated.emit(1, { status: 'loading' });
    assert.equal((nativeOut.at(-1)!.tabs as { tabId: number }[]).some(tab => tab.tabId === 1), false);
    await Promise.all(runtimeMessage.emit({ type: 'player.gone', instance: 'stale' }, { id: api.runtime.id, tab: live.get(2), frameId: 0 }));
    assert.equal((nativeOut.at(-1)!.tabs as unknown[]).length, 1, 'stale document removal cannot remove current state');
    removed.emit(2);
    assert.equal((nativeOut.at(-1)!.tabs as unknown[]).length, 0);
    nativeMessage.emit({ version: 2, type: 'browser.command', id: 'bad', command: 'playPause' });
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(nativeOut.at(-1)!.ok, false);
    assert.equal(playerCommands.length, 1, 'invalid command must never reach a player');
    nativeError = { message: 'No such native application com.bedside.remote' };
    nativeDisconnect.emit();
    const disconnected = await runtimeMessage.emit({ type: 'popup.status' }, { id: api.runtime.id, url: api.runtime.getURL('popup.html') })[0] as { connected: boolean; error: string };
    assert.equal(disconnected.connected, false);
    assert.match(disconnected.error, /No such native application com\.bedside\.remote/, 'the popup must expose the actual browser disconnect reason');
    nativeError = undefined;
    await new Promise(resolve => setTimeout(resolve, 1100));
    assert.equal(connections, 2, 'native host disconnect retries with bounded backoff');
    assert.equal(nativeOut.at(-1)!.type, 'browser.state', 'reconnection sends a fresh snapshot, never replays player commands');
    assert.equal(playerCommands.length, 1);
    for (let index = 100; index < 133; index++) {
      live.set(index, { id: index, url, active: false });
      await report(index, `tab-${index}`, state('youtube'));
    }
    await Promise.all(runtimeMessage.emit({ type: 'popup.select', tabId: 132 }, { id: api.runtime.id, url: api.runtime.getURL('popup.html') }));
    const bounded = nativeOut.at(-1)!;
    assert.equal((bounded.tabs as unknown[]).length, 32, '33 compatible tabs obey the native parser limit');
    assert.equal((bounded.tabs as { tabId: number }[])[0]!.tabId, 132, 'selected tab survives truncation even when added last');
    assert.equal(bounded.activeTabId, 132);
  } finally { for (const timer of timers) clearTimeout(timer); }
});
