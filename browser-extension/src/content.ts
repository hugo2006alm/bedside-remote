import { NetflixAdapter } from './adapters/netflix';
import { YoutubeAdapter } from './adapters/youtube';
import { object, onlyKeys, parseNativeCommand, platformForUrl } from './protocol';

const platform = platformForUrl(location.href);
const adapter = platform === 'youtube' ? new YoutubeAdapter(document) : platform === 'netflix' ? new NetflixAdapter(document) : null;
if (adapter) {
  let observedVideo: HTMLVideoElement | null = null;
  let identity = location.href;
  let ended = false;
  let error: string | undefined;
  let lastState = '';
  let lastSent = 0;
  let busy = false;
  let stopped = false;
  const instance = crypto.randomUUID();
  const naturalEnd = (event: Event) => {
    if (!event.isTrusted || !observedVideo?.ended || identity !== location.href || observedVideo !== adapter.video() ||
        document.querySelector('#movie_player.ad-showing, #movie_player.ad-interrupting')) return;
    ended = true;
    void publish(true);
  };
  const resumed = () => { ended = false; error = undefined; void publish(true); };
  const reset = () => { ended = false; void publish(true); };
  async function publish(force = false) {
    if (stopped) return;
    if (identity !== location.href) { identity = location.href; ended = false; error = undefined; }
    const video = adapter!.video();
    if (observedVideo !== video) {
      observedVideo?.removeEventListener('ended', naturalEnd);
      observedVideo?.removeEventListener('playing', resumed);
      observedVideo?.removeEventListener('seeking', reset);
      observedVideo = video;
      ended = false;
      video?.addEventListener('ended', naturalEnd);
      video?.addEventListener('playing', resumed);
      video?.addEventListener('seeking', reset);
    }
    const state = adapter!.state(ended, error);
    const serialized = JSON.stringify(state);
    if (!force && (serialized === lastState || Date.now() - lastSent < 1000)) return;
    lastState = serialized;
    lastSent = Date.now();
    try { await browser.runtime.sendMessage({ type: 'player.state', instance, state }); }
    catch { /* Extension reload or sleeping background: next poll retries. */ lastState = ''; }
  }
  browser.runtime.onMessage.addListener((message: unknown) => {
    if (object(message) && message.type === 'player.snapshot' && onlyKeys(message, ['type'])) {
      return publish(true).then(() => ({ ok: true }));
    }
    if (!object(message) || message.type !== 'player.command') return undefined;
    if (!onlyKeys(message, ['type', 'command'])) return Promise.resolve({ ok: false, error: 'Invalid player message.' });
    return (async () => {
      if (busy) return { ok: false, error: 'The player is processing another action.' };
      busy = true;
      try {
        const command = parseNativeCommand(message.command);
        if (command.command === 'openYoutube') throw new Error('Navigation must be handled by the background.');
        await adapter!.execute(command.command, command.position);
        error = undefined;
        return { ok: true };
      } catch (cause) {
        error = cause instanceof Error ? cause.message : 'Player action failed.';
        return { ok: false, error };
      } finally { busy = false; await publish(true); }
    })();
  });
  let timer = setInterval(() => { void publish(); }, 1000);
  // No DOM-wide mutation observer: streaming pages change constantly. The one-second
  // sampler discovers SPA navigation, replacement video elements and contextual controls.
  window.addEventListener('pagehide', () => {
    stopped = true;
    clearInterval(timer);
    void browser.runtime.sendMessage({ type: 'player.gone', instance }).catch(() => {});
  });
  window.addEventListener('pageshow', event => {
    if (event.persisted) {
      stopped = false;
      timer = setInterval(() => { void publish(); }, 1000);
      void publish(true);
    }
  });
  void publish(true);
}
