import type { Browser } from '@e2e-dev/web';

type Point = {
  x: number;
  y: number;
  hit: boolean;
};

type DragPoints = {
  from: Point;
  to: Point;
};

/**
 * Real pointer drag with intermediate moves. Playwright's `locator.dragTo` presses, jumps once to the target and
 * releases; CDK drag-drop treats its first over-threshold move only as "drag started", so a one-jump drag can never
 * reorder a CDK list. Geometry is only read here (after scrolling the source into view, before any pointer input);
 * every pointer event comes from the browser mouse, and the start and end points must really hit their elements.
 */
export async function pointerDrag(
  browser: Browser,
  fromSelector: string,
  toSelector: string,
  steps = 12,
): Promise<void> {
  // rowser.evaluate is typed as plain JSON; the callback below returns exactly DragPoints.
  const points = (await browser.evaluate(
    (arg: { from: string; to: string }) => {
      const source = document.querySelector(arg.from);
      const target = document.querySelector(arg.to);
      if (!source || !target)
        throw new Error('Missing drag element for ' + arg.from + ' or ' + arg.to);
      // Centre the span between both ends in the nearest scrollable ancestor so neither end is under fixed chrome.
      let scroller: Element | null = source.parentElement;
      while (scroller && scroller.scrollHeight <= scroller.clientHeight + 1)
        scroller = scroller.parentElement;
      const [a, b] = [source.getBoundingClientRect(), target.getBoundingClientRect()];
      const middle = (a.top + a.height / 2 + b.top + b.height / 2) / 2;
      if (scroller) scroller.scrollBy(0, middle - window.innerHeight / 2);
      const point = (el: Element): Point => {
        const r = el.getBoundingClientRect();
        const x = r.left + r.width / 2;
        const y = r.top + r.height / 2;
        const top = document.elementFromPoint(x, y);
        return { x, y, hit: !!top && el.contains(top) };
      };
      return { from: point(source), to: point(target) };
    },
    { from: fromSelector, to: toSelector },
  )) as unknown as DragPoints;
  if (!points.from.hit || !points.to.hit)
    throw new Error('Drag point is covered or off screen: ' + JSON.stringify(points));
  await browser.mouse.move(points.from.x, points.from.y);
  await browser.mouse.down();
  for (let i = 1; i <= steps; i++) {
    await browser.mouse.move(
      points.from.x + ((points.to.x - points.from.x) * i) / steps,
      points.from.y + ((points.to.y - points.from.y) * i) / steps,
    );
  }
  await browser.mouse.up();
}
