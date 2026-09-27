/**
 * What a stored file is, for display purposes.
 *
 * Distinct from `MEDIA_KINDS` in `dynamic-form/formly-preview.types.ts`: that one says which
 * *capture* mode a form question runs in, this one says how an existing file should be *rendered*.
 * A survey's file list has no question behind it, only a content type, and it can legitimately
 * hold something that is none of the three — hence `Other`.
 */
export const MEDIA_VIEW_KINDS = {
  Image: 'image',
  Video: 'video',
  Audio: 'audio',
  Other: 'other',
} as const;

export type MediaViewKind = (typeof MEDIA_VIEW_KINDS)[keyof typeof MEDIA_VIEW_KINDS];

/**
 * One file the viewer can show.
 *
 * Exactly one source is needed: `url` for a blob that already exists locally (a file the user has
 * just picked, whose bytes never left the browser), otherwise `fileId` for a stored file, fetched
 * on demand through `MediaObjectUrlService`.
 */
export interface MediaItem {
  /** Server handle. Resolved to a blob object URL when `url` is absent. */
  readonly fileId?: string;
  /** An object URL the caller already owns; used as-is, never revoked by the viewer. */
  readonly url?: string;
  readonly name: string;
  readonly contentType?: string;
  readonly sizeBytes?: number;
}

const MIME_PREFIXES: readonly (readonly [string, MediaViewKind])[] = [
  ['image/', MEDIA_VIEW_KINDS.Image],
  ['video/', MEDIA_VIEW_KINDS.Video],
  ['audio/', MEDIA_VIEW_KINDS.Audio],
];

/**
 * Extensions used when the content type is missing or generic. Not exhaustive — anything unlisted
 * falls through to `Other`, which still previews nothing but downloads fine.
 */
const EXTENSION_KINDS: Readonly<Record<string, MediaViewKind>> = {
  jpg: MEDIA_VIEW_KINDS.Image,
  jpeg: MEDIA_VIEW_KINDS.Image,
  png: MEDIA_VIEW_KINDS.Image,
  gif: MEDIA_VIEW_KINDS.Image,
  webp: MEDIA_VIEW_KINDS.Image,
  bmp: MEDIA_VIEW_KINDS.Image,
  heic: MEDIA_VIEW_KINDS.Image,
  svg: MEDIA_VIEW_KINDS.Image,
  mp4: MEDIA_VIEW_KINDS.Video,
  mov: MEDIA_VIEW_KINDS.Video,
  webm: MEDIA_VIEW_KINDS.Video,
  avi: MEDIA_VIEW_KINDS.Video,
  mkv: MEDIA_VIEW_KINDS.Video,
  '3gp': MEDIA_VIEW_KINDS.Video,
  mp3: MEDIA_VIEW_KINDS.Audio,
  m4a: MEDIA_VIEW_KINDS.Audio,
  wav: MEDIA_VIEW_KINDS.Audio,
  ogg: MEDIA_VIEW_KINDS.Audio,
  aac: MEDIA_VIEW_KINDS.Audio,
  amr: MEDIA_VIEW_KINDS.Audio,
  opus: MEDIA_VIEW_KINDS.Audio,
};

const ICONS: Readonly<Record<MediaViewKind, string>> = {
  [MEDIA_VIEW_KINDS.Image]: 'pi pi-image',
  [MEDIA_VIEW_KINDS.Video]: 'pi pi-video',
  [MEDIA_VIEW_KINDS.Audio]: 'pi pi-volume-up',
  [MEDIA_VIEW_KINDS.Other]: 'pi pi-file',
};

/**
 * Classifies a file by content type, falling back to its extension.
 *
 * The fallback matters: the upload row defaults to `application/octet-stream` whenever the browser
 * sent no type, which is common for recordings captured on a device.
 */
export function mediaKindOf(contentType?: string, fileName?: string): MediaViewKind {
  const mime = contentType?.trim().toLowerCase() ?? '';
  const match = MIME_PREFIXES.find(([prefix]) => mime.startsWith(prefix));
  if (match) {
    return match[1];
  }

  return EXTENSION_KINDS[extensionOf(fileName ?? '')] ?? MEDIA_VIEW_KINDS.Other;
}

/** Convenience overload for the common case of classifying a whole item. */
export function mediaKindOfItem(item: MediaItem): MediaViewKind {
  return mediaKindOf(item.contentType, item.name);
}

export function mediaIconOf(kind: MediaViewKind): string {
  return ICONS[kind];
}

/** `1536` -> `1.5 KB`. */
export function formatFileSize(bytes: number): string {
  if (!bytes) {
    return '0 B';
  }

  const units = ['B', 'KB', 'MB', 'GB'];
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  return `${parseFloat((bytes / Math.pow(1024, exponent)).toFixed(1))} ${units[exponent]}`;
}

/** `photo.final.JPG` -> `jpg`; '' when the name has no extension. */
function extensionOf(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot >= 0 ? fileName.slice(dot + 1).toLowerCase() : '';
}
