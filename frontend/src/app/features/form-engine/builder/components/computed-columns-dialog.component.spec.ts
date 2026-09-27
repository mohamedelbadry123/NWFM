import { toKey } from './computed-columns-dialog.component';

describe('toKey', () => {
  it('turns a title into a column key', () => {
    expect(toKey('Leak Severity')).toBe('leak_severity');
    expect(toKey('  Water lost (m³)  ')).toBe('water_lost_m3');
    expect(toKey('Café-Status')).toBe('cafe_status');
  });

  it('starts with a letter, and falls back when the title has no Latin letters', () => {
    expect(toKey('2nd visit')).toBe('c_2nd_visit');
    expect(toKey('الخطورة')).toBe('column');
    expect(toKey('')).toBe('column');
  });

  it('never exceeds the key length', () => {
    expect(toKey('x'.repeat(100)).length).toBe(64);
  });
});
