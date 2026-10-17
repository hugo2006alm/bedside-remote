export const HOST_NAME = 'com.bedside.remote';
export const COMMANDS = ['playPause', 'seek', 'skipIntro', 'skipRecap', 'nextEpisode', 'fullscreen', 'subtitles', 'audio'] as const;
export type Command = typeof COMMANDS[number];
export interface Playback {
  platform: 'youtube' | 'netflix';
  tabId: number;
  videoId?: string;
  title: string;
  channel?: string;
  thumbnail?: string;
  paused: boolean;
  position: number;
  duration: number;
  ended: boolean;
  capabilities: Command[];
  error?: string;
}
export type PlayerState = Omit<Playback, 'tabId'>;
export interface NativeCommand {
  version: 1;
  type: 'browser.command';
  id: string;
  tabId?: number;
  command: Command | 'openYoutube' | 'selectTab';
  position?: number;
  url?: string;
}
export interface Result { ok: boolean; error?: string }
export function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
export function onlyKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  return Object.keys(value).every(key => keys.includes(key));
}
export function parseNativeCommand(value: unknown): NativeCommand {
  if (!object(value) || !onlyKeys(value, ['version', 'type', 'id', 'tabId', 'command', 'position', 'url']) ||
      value.version !== 1 || value.type !== 'browser.command' || typeof value.id !== 'string' || !value.id || value.id.length > 128 ||
      (value.tabId !== undefined && (!Number.isSafeInteger(value.tabId) || (value.tabId as number) < 0)) ||
      typeof value.command !== 'string' || ![...COMMANDS, 'openYoutube', 'selectTab'].includes(value.command)) throw new Error('Invalid browser command.');
  if (value.command === 'selectTab' && value.tabId === undefined) throw new Error('Tab selection requires a tab ID.');
  if (value.command === 'seek') {
    if (typeof value.position !== 'number' || !Number.isFinite(value.position) || value.position < 0 || value.url !== undefined) throw new Error('Invalid seek position.');
  } else if (value.position !== undefined) throw new Error('Unexpected position.');
  if (value.command === 'openYoutube') {
    if (typeof value.url !== 'string' || canonicalYoutube(value.url) !== value.url) throw new Error('Expected canonical YouTube video URL.');
  } else if (value.url !== undefined) throw new Error('Unexpected URL.');
  return value as unknown as NativeCommand;
}
export function canonicalYoutube(input: string): string {
  if (input.length > 4096 || /[\u0000-\u0020\u007f\\]/.test(input)) throw new Error('Invalid YouTube URL.');
  const url = new URL(input);
  const hosts = ['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com', 'youtu.be'];
  if (url.protocol !== 'https:' || !hosts.includes(url.hostname) || url.username || url.password || url.port) throw new Error('Invalid YouTube host.');
  let id: string | null = null;
  if (url.hostname === 'youtu.be') id = /^\/([A-Za-z0-9_-]{11})\/?$/.exec(url.pathname)?.[1] ?? null;
  else if (url.pathname === '/watch' && url.searchParams.getAll('v').length === 1) id = url.searchParams.get('v');
  else id = /^\/(?:shorts|live|embed)\/([A-Za-z0-9_-]{11})\/?$/.exec(url.pathname)?.[1] ?? null;
  if (!id || !/^[A-Za-z0-9_-]{11}$/.test(id)) throw new Error('Expected YouTube video ID.');
  return `https://www.youtube.com/watch?v=${id}`;
}
export function platformForUrl(input: string): Playback['platform'] | undefined {
  try {
    const url = new URL(input);
    if (url.protocol !== 'https:' || url.username || url.password || url.port) return undefined;
    if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com'].includes(url.hostname)) return 'youtube';
    if (['netflix.com', 'www.netflix.com'].includes(url.hostname)) return 'netflix';
  } catch { /* An invalid URL is not a compatible tab. */ }
  return undefined;
}
export function isPlayerState(value: unknown): value is PlayerState {
  if (!object(value) || !onlyKeys(value, ['platform', 'videoId', 'title', 'channel', 'thumbnail', 'paused', 'position', 'duration', 'ended', 'capabilities', 'error'])) return false;
  return ['youtube', 'netflix'].includes(value.platform as string) && typeof value.title === 'string' && value.title.length <= 512 &&
    ['channel', 'thumbnail', 'error'].every(key => value[key] === undefined || (typeof value[key] === 'string' && (value[key] as string).length <= 1024)) &&
    (value.videoId === undefined || (typeof value.videoId === 'string' && /^[A-Za-z0-9_-]{11}$/.test(value.videoId))) &&
    typeof value.paused === 'boolean' && typeof value.ended === 'boolean' &&
    typeof value.position === 'number' && Number.isFinite(value.position) && value.position >= 0 &&
    typeof value.duration === 'number' && Number.isFinite(value.duration) && value.duration >= 0 &&
    Array.isArray(value.capabilities) && value.capabilities.every(item => COMMANDS.includes(item)) && value.capabilities.length <= COMMANDS.length;
}
