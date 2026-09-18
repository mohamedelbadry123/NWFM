export function prettyWorkflowLabel(key: string | null | undefined): string {
  if (!key) return '—';
  return key
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\d{8,}/g, '')
    .replace(/\s+/g, ' ')
    .trim() || '—';
}

export function shortId(id: string | null | undefined): string {
  if (!id) return '—';
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
