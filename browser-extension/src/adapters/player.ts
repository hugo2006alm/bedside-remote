import type { Command, PlayerState } from '../protocol';

export interface Adapter {
  state(ended: boolean, error?: string): PlayerState;
  video(): HTMLVideoElement | null;
  execute(command: Command, position?: number): Promise<void>;
}
export interface Selectors {
  title: string;
  player: string;
  controls: Partial<Record<Command, string>>;
}
export function visible(element: HTMLElement | null): element is HTMLElement {
  if (!element || element.hidden || element.closest('[hidden], [aria-hidden="true"]') || element.getAttribute('aria-disabled') === 'true' || element.matches(':disabled')) return false;
  const style = element.ownerDocument.defaultView?.getComputedStyle(element);
  return style?.display !== 'none' && style?.visibility !== 'hidden' && element.getClientRects().length > 0;
}
export abstract class DomAdapter implements Adapter {
  constructor(protected doc: Document, protected selectors: Selectors) {}
  abstract platform: PlayerState['platform'];
  abstract metadata(): Pick<PlayerState, 'title' | 'videoId' | 'channel' | 'thumbnail'>;
  video(): HTMLVideoElement | null { return this.doc.querySelector('video'); }
  protected button(command: Command): HTMLElement | null {
    const selector = this.selectors.controls[command];
    return selector ? Array.from(this.doc.querySelectorAll<HTMLElement>(selector)).find(visible) ?? null : null;
  }
  state(ended: boolean, error?: string): PlayerState {
    const video = this.video();
    const duration = video && Number.isFinite(video.duration) && video.duration > 0 ? video.duration : 0;
    const capabilities: Command[] = [];
    if (video && video.readyState > 0 && !video.error) {
      capabilities.push('playPause');
      if (duration > 0 && video.seekable.length > 0) capabilities.push('seek');
    }
    // Fullscreen needs transient user activation; a remote command cannot supply it.
    // Exiting an existing fullscreen is safe and does not require user activation.
    if (this.doc.fullscreenElement) capabilities.push('fullscreen');
    for (const command of ['skipIntro', 'skipRecap', 'nextEpisode', 'subtitles', 'audio'] as const) {
      if (this.button(command)) capabilities.push(command);
    }
    return {
      platform: this.platform, ...this.metadata(), paused: video?.paused ?? true,
      position: video && Number.isFinite(video.currentTime) ? Math.max(0, video.currentTime) : 0,
      duration, ended: !!video && ended, capabilities,
      ...((error || video?.error) ? { error: error ?? `Media playback error (${video?.error?.code}).` } : {})
    };
  }
  async execute(command: Command, position?: number): Promise<void> {
    const video = this.video();
    if (command === 'fullscreen') {
      if (this.doc.fullscreenElement) await this.doc.exitFullscreen();
      else throw new Error('Fullscreen requires a click in the browser player. Use the universal remote keyboard fallback.');
      return;
    }
    if (!this.state(false).capabilities.includes(command)) throw new Error('This player action is currently unavailable.');
    if (command === 'playPause' && video) {
      if (video.paused) {
        try { await video.play(); }
        catch { throw new Error('Playback was blocked. Allow autoplay or press Play in the browser first.'); }
        if (video.paused) throw new Error('The player did not start playback.');
      } else {
        video.pause();
        if (!video.paused) throw new Error('The player did not pause.');
      }
      return;
    }
    if (command === 'seek' && video && typeof position === 'number' && Number.isFinite(position) && position >= 0) {
      if (position > video.duration) throw new Error('Seek position exceeds video duration.');
      let seekable = false;
      for (let index = 0; index < video.seekable.length; index++) {
        if (position >= video.seekable.start(index) && position <= video.seekable.end(index)) seekable = true;
      }
      if (!seekable) throw new Error('Seek position is outside the available media range.');
      video.currentTime = position;
      return;
    }
    const button = this.button(command);
    if (!button) throw new Error('This player action is currently unavailable.');
    const beforePosition = video?.currentTime ?? 0;
    const beforeUrl = this.doc.location.href;
    const beforeVideo = video?.currentSrc;
    const expanded = button.getAttribute('aria-expanded');
    button.click();
    await new Promise(resolve => setTimeout(resolve, 350));
    if (command === 'skipIntro' || command === 'skipRecap') {
      if (video && Math.abs(video.currentTime - beforePosition) > 1) return;
    } else if (command === 'nextEpisode') {
      if (this.doc.location.href !== beforeUrl || this.video()?.currentSrc !== beforeVideo) return;
    } else if (command === 'subtitles' || command === 'audio') {
      if (button.getAttribute('aria-expanded') !== expanded ||
          Array.from(this.doc.querySelectorAll<HTMLElement>('[role="menu"], [role="dialog"], .ytp-settings-menu, [data-uia="audio-subtitle-controller"]')).some(visible)) return;
    }
    throw new Error('The player did not confirm this action. Use the universal remote fallback.');
  }
}
