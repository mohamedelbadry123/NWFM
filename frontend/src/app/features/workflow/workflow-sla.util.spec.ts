import { formatRemainingSla, formatSlaDuration, remainingSlaTone } from './workflow-sla.util';

describe('formatRemainingSla', () => {
  it('formats hours and minutes with zero padding', () => {
    expect(formatRemainingSla(272)).toBe('04h 32m');
  });

  it('prefixes a minus sign when SLA is breached', () => {
    expect(formatRemainingSla(-15)).toBe('-00h 15m');
  });

  it('returns an em dash when minutes are missing', () => {
    expect(formatRemainingSla(null)).toBe('—');
    expect(formatRemainingSla(undefined)).toBe('—');
  });
});

describe('formatSlaDuration', () => {
  it('always formats as a positive duration', () => {
    expect(formatSlaDuration(-90)).toBe('01h 30m');
  });
});

describe('remainingSlaTone', () => {
  it('uses warning when one hour or less remains', () => {
    expect(remainingSlaTone(45, 'Running')).toBe('warning');
    expect(remainingSlaTone(60, 'Pending')).toBe('warning');
  });

  it('uses ok when more than one hour remains', () => {
    expect(remainingSlaTone(190, 'Running')).toBe('ok');
  });

  it('uses breached for negative remaining time', () => {
    expect(remainingSlaTone(-25, 'Running')).toBe('breached');
  });

  it('uses closed for completed or cancelled work', () => {
    expect(remainingSlaTone(10, 'Completed')).toBe('closed');
    expect(remainingSlaTone(-5, 'Cancelled')).toBe('closed');
  });
});
