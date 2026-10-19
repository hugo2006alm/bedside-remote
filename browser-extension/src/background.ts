import { NativeLink } from './native';
import { listen } from './messages';
import { isPlayerState, object, onlyKeys, parseNativeCommand, platformForUrl, type NativeCommand, type Playback, type Result } from './protocol';

interface TabState { instance: string; state: Playback }
const tabs = new Map<number, TabState>();
let activeTabId: number | undefined;
let pendingActiveTabId: number | undefined;
let commands = Promise.resolve();
let progressTimer: ReturnType<typeof setTimeout> | undefined;
let lastPublish = 0;
const native = new NativeLink(message => {
  commands = commands.then(() => receiveNative(message)).catch(() => {});
}, publish);

function snapshot() {
  const ordered = [...tabs.values()].map(item => item.state)
    .sort((a, b) => Number(b.tabId === activeTabId) - Number(a.tabId === activeTabId)).slice(0, 32);
  const message: { version: 1; type: 'browser.state'; activeTabId?: number; tabs: Playback[] } = {
    version: 1, type: 'browser.state', ...(activeTabId !== undefined && tabs.has(activeTabId) ? { activeTabId } : {}), tabs: []
  };
  for (const state of ordered) {
    message.tabs.push(state);
    if (new TextEncoder().encode(JSON.stringify(message)).length > 65_536) { message.tabs.pop(); break; }
  }
  return message;
}
function publish() {
  if (progressTimer) { clearTimeout(progressTimer); progressTimer = undefined; }
  lastPublish = Date.now();
  native.send(snapshot());
}
function publishProgress() {
  if (Date.now() - lastPublish >= 1000) publish();
  else if (!progressTimer) progressTimer = setTimeout(publish, 1000 - (Date.now() - lastPublish));
}
function forget(tabId: number) {
  if (!tabs.delete(tabId)) return;
  if (activeTabId === tabId) activeTabId = tabs.keys().next().value;
  publish();
}
async function execute(command: NativeCommand): Promise<Result> {
  if (command.command === 'selectTab') {
    if (command.tabId === undefined || !tabs.has(command.tabId)) throw new Error('The selected compatible tab is no longer available.');
    activeTabId = command.tabId;
    pendingActiveTabId = undefined;
    publish();
    return { ok: true };
  }
  if (command.command === 'openYoutube') {
    const url = command.url!;
    const videoId = new URL(url).searchParams.get('v');
    let targetId = command.tabId;
    if (targetId !== undefined && tabs.get(targetId)?.state.platform !== 'youtube') throw new Error('The selected YouTube tab is no longer available.');
    targetId ??= (activeTabId !== undefined && tabs.get(activeTabId)?.state.platform === 'youtube' ? activeTabId : undefined)
      ?? [...tabs.values()].find(item => item.state.platform === 'youtube')?.state.tabId;
    if (targetId !== undefined) {
      // Clear the old natural-ended latch before navigation; never advance for reload.
      forget(targetId);
      await browser.tabs.update(targetId, { url, active: true });
    } else {
      targetId = (await browser.tabs.create({ url, active: true })).id;
    }
    if (targetId === undefined) throw new Error('The browser did not create a YouTube tab.');
    activeTabId = targetId;
    const deadline = Date.now() + 12_000;
    while (Date.now() < deadline) {
      const state = tabs.get(targetId)?.state;
      if (state?.videoId === videoId && state.capabilities.includes('playPause')) {
        if (state.paused) return sendPlayer(targetId, { ...command, command: 'playPause', url: undefined });
        return { ok: true };
      }
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    throw new Error('YouTube opened, but the player is not ready. Check the tab, consent prompts and autoplay settings.');
  }
  const targetId = command.tabId ?? activeTabId;
  if (targetId === undefined || !tabs.has(targetId)) throw new Error('No compatible player tab is available.');
  activeTabId = targetId;
  pendingActiveTabId = undefined;
  publish();
  return sendPlayer(targetId, command);
}
async function sendPlayer(tabId: number, command: NativeCommand): Promise<Result> {
  let timeout: ReturnType<typeof setTimeout> | undefined;
  try {
    const result: unknown = await Promise.race([
      browser.tabs.sendMessage(tabId, { type: 'player.command', command }, { frameId: 0 }),
      new Promise<never>((_, reject) => { timeout = setTimeout(() => reject(new Error('The player did not respond.')), 5000); })
    ]);
    if (!object(result) || typeof result.ok !== 'boolean' || !onlyKeys(result, ['ok', 'error']) ||
        (result.error !== undefined && typeof result.error !== 'string')) throw new Error('Invalid player response.');
    return result as unknown as Result;
  } finally { if (timeout) clearTimeout(timeout); }
}
async function receiveNative(value: unknown) {
  let command: NativeCommand;
  try { command = parseNativeCommand(value); }
  catch {
    if (object(value) && typeof value.id === 'string' && value.id.length <= 128) native.send({ version: 1, type: 'browser.result', id: value.id, ok: false, error: 'Invalid browser command.' });
    return;
  }
  let result: Result;
  try { result = await execute(command); }
  catch (cause) { result = { ok: false, error: cause instanceof Error ? cause.message : 'Browser action failed.' }; }
  native.send({ version: 1, type: 'browser.result', id: command.id, ...result });
}

listen((message: unknown, sender) => {
  if (!object(message)) return undefined;
  if (sender.id !== browser.runtime.id) return undefined;
  if (message.type === 'player.state' && sender.tab?.id !== undefined && sender.frameId === 0) {
    const tabId = sender.tab.id;
    if (!onlyKeys(message, ['type', 'instance', 'state']) || typeof message.instance !== 'string' || message.instance.length > 64 || !isPlayerState(message.state) || platformForUrl(sender.url ?? '') !== message.state.platform) return undefined;
    const state = message.state;
    const instance = message.instance;
    return browser.tabs.get(tabId).then(async tab => {
      if (platformForUrl(tab.url ?? '') !== state.platform) return { ok: false };
      // SPA routes can change while sender.url still names the original document.
      // Ask the current top document to prove its instance before admitting a new
      // instance or a URL mismatch; late messages from replaced documents fail.
      if (tabs.get(tabId)?.instance !== instance || tab.url !== sender.url) {
        const current: unknown = await browser.tabs.sendMessage(tabId, { type: 'player.identity' }, { frameId: 0 });
        if (!object(current) || current.instance !== instance || current.url !== tab.url) return { ok: false };
      }
      const previous = tabs.get(tabId)?.state;
      tabs.set(tabId, { instance, state: { ...state, tabId } });
      if (activeTabId === undefined || pendingActiveTabId === tabId) {
        activeTabId = tabId;
        if (pendingActiveTabId === tabId) pendingActiveTabId = undefined;
      }
      const stable = (value: typeof state | undefined) => value ? JSON.stringify({ ...value, tabId: undefined, position: 0 }) : '';
      if (stable(previous) === stable(state)) publishProgress();
      else publish();
      return { ok: true };
    }).catch(() => ({ ok: false }));
  }
  if (message.type === 'player.gone' && sender.tab?.id !== undefined && sender.frameId === 0) {
    if (tabs.get(sender.tab.id)?.instance === message.instance) forget(sender.tab.id);
    return undefined;
  }
  // Popup messages are accepted only from extension pages, never player frames.
  if (sender.tab || !sender.url?.startsWith(browser.runtime.getURL(''))) return undefined;
  if (message.type === 'popup.status' && onlyKeys(message, ['type'])) {
    return Promise.resolve({ connected: native.connected, connecting: native.connecting, error: native.lastError, activeTabId, tabs: [...tabs.values()].map(item => item.state) });
  }
  if (message.type === 'popup.select' && onlyKeys(message, ['type', 'tabId']) && typeof message.tabId === 'number' && tabs.has(message.tabId)) {
    activeTabId = message.tabId;
    pendingActiveTabId = undefined;
    publish();
    return Promise.resolve({ ok: true });
  }
  if (message.type === 'popup.reconnect' && onlyKeys(message, ['type'])) {
    native.reconnect();
    return Promise.resolve({ ok: true });
  }
  return undefined;
});
browser.tabs.onRemoved.addListener(forget);
browser.tabs.onUpdated.addListener((tabId, change) => {
  if (change.status === 'loading' || change.url) {
    const selected = activeTabId === tabId || pendingActiveTabId === tabId;
    forget(tabId);
    if (selected) pendingActiveTabId = tabId;
    // A History API route does not reload its content script. Request a fresh
    // snapshot even when the media values happen to equal the previous snapshot.
    void browser.tabs.sendMessage(tabId, { type: 'player.snapshot' }, { frameId: 0 }).catch(() => {});
  }
});
browser.tabs.onActivated.addListener(({ tabId }) => {
  if (tabs.has(tabId)) { activeTabId = tabId; pendingActiveTabId = undefined; publish(); }
  else pendingActiveTabId = tabId;
});
// Alarms survive event-page suspension, unlike retry timers. A live native port
// keeps the page alive; the alarm restores it after browser/host restart.
browser.alarms.onAlarm.addListener(alarm => { if (alarm.name === 'native-reconnect' && !native.connected) native.connect(); });
void browser.alarms.create('native-reconnect', { periodInMinutes: 0.5 });
native.connect();
void browser.tabs.query({ url: browser.runtime.getManifest().host_permissions }).then(async existing => {
  const foreground = await browser.tabs.query({ active: true, currentWindow: true });
  if (activeTabId === undefined) pendingActiveTabId = foreground[0]?.id;
  for (const tab of existing) {
    if (tab.id !== undefined) void browser.tabs.sendMessage(tab.id, { type: 'player.snapshot' }, { frameId: 0 }).catch(() => {});
  }
}).catch(() => {});
