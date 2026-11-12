import { describe, expect, it, vi } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowBusinessActivityComponent } from './workflow-business-activity.component';
import { WorkflowWorkspaceService } from './workflow-workspace.service';

function editor(main: boolean, configuration: object) {
  TestBed.configureTestingModule({
    providers: [
      { provide: LocaleService, useValue: { locale: () => 'en' } },
      { provide: WorkflowWorkspaceService, useValue: { references: () => of([]) } },
    ],
  });
  const component = TestBed.runInInjectionContext(() => new WorkflowBusinessActivityComponent());
  component.main = main;
  component.parentSettings = { kind: main ? 'Main' : 'Child', designerVersion: 3 };
  component.configuration = JSON.stringify(configuration);
  component.ngOnChanges();
  return component;
}

describe('Hierarchy data mapping contracts', () => {
  it('preserves child pins and uses child variable paths for Task Type outputs', () => {
    const component = editor(true, {
      versionId: 'published-child',
      outputMappings: { releaseId: 'contractorReleaseId' },
    });
    expect(component.dataMappings.output).toEqual([
      { target: 'releaseId', source: 'contractorReleaseId' },
    ]);
    component.dataMappings.input.push({ target: 'reference', source: 'requestReference' });
    const saved = vi.fn();
    component.configurationChange.subscribe(saved);
    component.saveMappings();
    expect(JSON.parse(saved.mock.calls[0][0])).toEqual({
      versionId: 'published-child',
      inputMappings: { reference: 'requestReference' },
      outputMappings: { releaseId: 'contractorReleaseId' },
    });
  });
  it('keeps form submissions separate from their mapped workflow variables', () => {
    const component = editor(false, {
      formSteps: [{ id: 'one' }],
      outputMappingJson: '{"meterSerial":"output.meter_serial"}',
    });
    component.dataMappings.output.push({ target: 'notes', source: 'output.notes' });
    const saved = vi.fn();
    component.configurationChange.subscribe(saved);
    component.saveMappings();
    const configuration = JSON.parse(saved.mock.calls[0][0]);
    expect(configuration.formSteps).toEqual([{ id: 'one' }]);
    expect(JSON.parse(configuration.outputMappingJson)).toEqual({
      meterSerial: 'output.meter_serial',
      notes: 'output.notes',
    });
  });
  it('does not silently overwrite a variable with a duplicate destination', () => {
    const component = editor(true, { outputMappings: { result: 'original' } });
    component.dataMappings.output.push({ target: ' result ', source: 'replacement' });
    const saved = vi.fn();
    component.configurationChange.subscribe(saved);
    component.saveMappings();
    expect(saved).not.toHaveBeenCalled();
    expect(component.mappingError()).toContain('unique');
    expect(component.config['outputMappings']).toEqual({ result: 'original' });
  });
  it('cannot change a published configuration', () => {
    const component = editor(true, {});
    component.readonly = true;
    component.dataMappings.output.push({ target: 'result', source: 'answer' });
    const saved = vi.fn();
    component.configurationChange.subscribe(saved);
    component.saveMappings();
    expect(saved).not.toHaveBeenCalled();
  });
});
