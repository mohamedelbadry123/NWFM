import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Toast } from 'primeng/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Toast],
  template: `
    <p-toast [breakpoints]="{ '640px': { width: '90vw' } }" />
    <router-outlet />
  `,
})
export class App {}
