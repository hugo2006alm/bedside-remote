import type { Playback } from './protocol';
const connection = document.querySelector<HTMLElement>('#connection')!;
const detail = document.querySelector<HTMLElement>('#detail')!;
const target = document.querySelector<HTMLSelectElement>('#target')!;
const player = document.querySelector<HTMLElement>('#player')!;
const reconnect = document.querySelector<HTMLButtonElement>('#reconnect')!;
interface Status { connected: boolean; error: string; activeTabId?: number; tabs: Playback[] }
async function refresh() {
  try {
    const status: Status = await browser.runtime.sendMessage({ type: 'popup.status' });
    connection.textContent = status.connected ? 'Windows agent connected' : 'Windows agent unavailable';
    detail.textContent = status.error || 'Control is available from your paired Android phone.';
    const selection = String(status.activeTabId ?? '');
    const options = status.tabs.map(tab => {
      const option = document.createElement('option');
      option.value = String(tab.tabId);
      option.textContent = `${tab.platform === 'youtube' ? 'YouTube' : 'Netflix'} · ${tab.title || 'Untitled player'}`;
      return option;
    });
    if (!options.length) { const empty = document.createElement('option'); empty.textContent = 'No compatible tabs'; options.push(empty); }
    target.replaceChildren(...options);
    target.disabled = !status.tabs.length;
    target.value = selection;
    const state = status.tabs.find(tab => tab.tabId === status.activeTabId);
    player.textContent = state?.error ?? (state ? `${state.paused ? 'Paused' : 'Playing'} · ${Math.floor(state.position)} seconds` : 'Waiting for a player.');
  } catch (cause) {
    connection.textContent = 'Extension background unavailable. Reopen this popup.';
    detail.textContent = cause instanceof Error ? cause.message.slice(0, 300) : 'The browser did not return a background response.';
  }
}
target.addEventListener('change', () => { void browser.runtime.sendMessage({ type: 'popup.select', tabId: Number(target.value) }).then(refresh); });
reconnect.addEventListener('click', () => { void browser.runtime.sendMessage({ type: 'popup.reconnect' }).then(refresh); });
void refresh();
setInterval(() => { if (document.activeElement !== target) void refresh(); }, 1000);
