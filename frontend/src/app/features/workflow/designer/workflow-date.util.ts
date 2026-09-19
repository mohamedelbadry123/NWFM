/** Convert a stored instant to the browser's local datetime input, without relabeling UTC. */
export function toLocalDateTime(value: string): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  const pad = (n: number) => String(n).padStart(2, '0');
  const fraction = date.getMilliseconds() ? `.${String(date.getMilliseconds()).padStart(3, '0')}` : '';
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}${fraction}`;
}

/** datetime-local values are local wall time; persist an actual UTC instant. */
export function toUtcDateTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toISOString();
}
