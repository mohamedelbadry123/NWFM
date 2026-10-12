import { afterEach, describe, expect, it } from 'vitest';

const fixtures: HTMLElement[] = [];
function element(classes: string, tag = 'div') {
  const node = document.createElement(tag);
  node.className = classes;
  document.body.appendChild(node);
  fixtures.push(node);
  return node;
}
afterEach(() => fixtures.splice(0).forEach((node) => node.remove()));

describe('Tailwind migration preserves existing template styles', () => {
  it('keeps the legacy corner radii', () => {
    expect(getComputedStyle(element('rounded')).borderRadius).toBe('4px');
    expect(getComputedStyle(element('rounded-sm')).borderRadius).toBe('2px');
    expect(getComputedStyle(element('rounded-lg')).borderRadius).toBe('8px');
  });
  it('keeps the small shadow and default shadow distinct', () => {
    expect(getComputedStyle(element('shadow-sm')).boxShadow).toContain(
      'rgba(0, 0, 0, 0.05) 0px 1px 2px 0px',
    );
    expect(getComputedStyle(element('shadow')).boxShadow).toContain(
      'rgba(0, 0, 0, 0.1) 0px 1px 3px 0px',
    );
    expect(getComputedStyle(element('backdrop-blur-sm')).backdropFilter).toBe('blur(4px)');
  });
  it('keeps brand colors and class-based dark mode', () => {
    const node = element('bg-primary dark:bg-surface-800');
    expect(getComputedStyle(node).backgroundColor).toBe('rgb(36, 80, 222)');
    const parent = element('dark');
    parent.appendChild(node);
    expect(getComputedStyle(node).backgroundColor).toBe('rgb(22, 29, 64)');
  });
  it('keeps keyboard focus outlines accessible with existing class names', () => {
    const node = element('focus:outline-none', 'button');
    node.focus();
    const style = getComputedStyle(node);
    expect(style.outlineStyle).toBe('solid');
    expect(style.outlineWidth).toBe('2px');
    expect(style.outlineOffset).toBe('2px');
    expect(style.outlineColor).toBe('rgba(0, 0, 0, 0)');
  });
  it('keeps logical positioning in Arabic layouts', () => {
    const node = element('absolute start-3');
    node.dir = 'rtl';
    expect(getComputedStyle(node).right).toBe('12px');
  });
  it('keeps arbitrary CSS variables and important prefix utilities', () => {
    const node = element('border border-[var(--app-border)] !px-3');
    node.style.setProperty('--app-border', '#123456');
    node.style.paddingLeft = '40px';
    const style = getComputedStyle(node);
    expect(style.borderTopColor).toBe('rgb(18, 52, 86)');
    expect(style.paddingLeft).toBe('12px');
  });
  it('keeps existing gradient utility names', () => {
    const node = element('bg-gradient-to-br from-primary to-navy');
    expect(getComputedStyle(node).backgroundImage).toContain('linear-gradient(');
  });
});
