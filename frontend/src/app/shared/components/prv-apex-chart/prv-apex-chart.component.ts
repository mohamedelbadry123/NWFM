import {
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  afterNextRender,
  effect,
  input,
} from '@angular/core';
import ApexCharts from 'apexcharts';

@Component({
  selector: 'prv-apex-chart',
  standalone: true,
  template: `<div #chartEl></div>`,
})
export class PrvApexChartComponent implements OnDestroy {
  readonly options = input.required<ApexCharts.ApexOptions>();

  @ViewChild('chartEl', { static: true }) private readonly chartEl!: ElementRef<HTMLDivElement>;

  private chart: ApexCharts | null = null;
  private ready = false;

  constructor() {
    afterNextRender(() => {
      this.chart = new ApexCharts(this.chartEl.nativeElement, this.options());
      void this.chart.render();
      this.ready = true;
    });

    effect(() => {
      const opts = this.options();
      if (this.ready && this.chart) {
        void this.chart.updateOptions(opts, true, true, true);
      }
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
