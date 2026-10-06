import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { LocaleService } from '@core/i18n/locale.service';
import { WorkflowLocationComponent } from './workflow-location.component';
import { WorkflowBusinessActivityComponent } from './workflow-business-activity.component';
import {
  ReferenceItem, WorkflowWorkspaceService, WorkspaceSettings,
  hasLegacyLocationConflict, hasRequiredLocation, normalizeWorkspaceSettings, workspaceLocation,
} from './workflow-workspace.service';
import { buildNWFMXml } from '../designer/workflow-designer.component';

describe('Workflow workspace contracts', () => {
  it('clears a selected FA and ignores the old department response when department changes', () => {
    const previous = new Subject<ReferenceItem[]>(); const next = new Subject<ReferenceItem[]>();
    TestBed.configureTestingModule({providers:[{provide:LocaleService,useValue:{locale:()=> 'en'}},{provide:WorkflowWorkspaceService,useValue:{references:(_kind:string,parent?:string)=>parent==='old'?previous:next}}]});
    const component = TestBed.runInInjectionContext(()=>new WorkflowBusinessActivityComponent());
    component.config={departmentCode:'old',fieldActivityCode:'OLD-FA'}; component.loadFields(); component.department('new');
    expect(component.config.fieldActivityCode).toBe('');
    next.next([{id:'2',code:'NEW-FA',nameEn:'New',nameAr:'',parentCode:'new'}]);
    previous.next([{id:'1',code:'OLD-FA',nameEn:'Old',nameAr:'',parentCode:'old'}]);
    expect(component.fieldTypes().map(x=>x.code)).toEqual(['NEW-FA']);
  });
  it('preserves workspace metadata when the full designer saves XML', () => {
    const workspace: WorkspaceSettings = { kind: 'Main', clusterCode:'CC', cbuCode:'RCBU', branchCode:'1110', operationAreaCode:'OA1', designerVersion: 2 };
    const xml = new DOMParser().parseFromString(buildNWFMXml({nodes:[],edges:[],variables:[],workspace}), 'application/xml');
    expect(JSON.parse(xml.documentElement.getAttribute('workspaceJson')!)).toEqual(workspace);
  });
});

describe('Workflow organization location', () => {
  describe('legacy settings', () => {
    it('reads the pre-hierarchy region and city as the CBU and branch they were chosen from', () => {
      const settings = normalizeWorkspaceSettings({ kind: 'Main', clusterCode: 'CC', regionCode: 'RCBU', cityCode: '1110', designerVersion: 2 });

      expect(settings).toEqual({ kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', branchCode: '1110', designerVersion: 2 });
      expect(hasLegacyLocationConflict(settings)).toBeFalse();
      expect(workspaceLocation(settings)).toEqual({ clusterCode: 'CC', cbuCode: 'RCBU', branchCode: '1110', operationAreaCode: null });
    });

    it('drops a legacy key that agrees with its replacement', () => {
      expect(normalizeWorkspaceSettings({ kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', regionCode: 'rcbu' }))
        .toEqual({ kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU' });
    });

    it('keeps both codes when they disagree, and never lets that count as a complete location', () => {
      const settings = normalizeWorkspaceSettings({ kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', regionCode: 'JCBU' });

      expect(settings.cbuCode).toBe('RCBU');
      expect(settings.regionCode).toBe('JCBU');
      expect(hasLegacyLocationConflict(settings)).toBeTrue();
      expect(hasRequiredLocation(settings)).toBeFalse();
    });

    it('treats a legacy child with null geography as a child with no place', () => {
      const settings = normalizeWorkspaceSettings(JSON.parse('{"kind":"Child","clusterCode":null,"regionCode":null,"cityCode":null}'));

      expect('regionCode' in settings || 'cityCode' in settings).toBeFalse();
      expect(hasLegacyLocationConflict(settings)).toBeFalse();
      expect(workspaceLocation(settings)).toEqual({ clusterCode: null, cbuCode: null, branchCode: null, operationAreaCode: null });
    });
  });

  it('requires at least a cluster and a CBU for a main workflow', () => {
    expect(hasRequiredLocation({ kind: 'Main' })).toBeFalse();
    expect(hasRequiredLocation({ kind: 'Main', clusterCode: 'CC' })).toBeFalse();
    expect(hasRequiredLocation({ kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU' })).toBeTrue();
    expect(hasRequiredLocation({ kind: 'Child' })).toBeTrue();
  });

  describe('WorkflowLocationComponent', () => {
    let component: WorkflowLocationComponent;
    let emitted: WorkspaceSettings[];

    beforeEach(() => {
      TestBed.configureTestingModule({ providers: [{ provide: LocaleService, useValue: { locale: () => 'en' } }] });
      component = TestBed.runInInjectionContext(() => new WorkflowLocationComponent());
      emitted = [];
      component.settingsChange.subscribe(s => emitted.push(s));
    });

    it('seeds the shared selector from settings arriving from outside', () => {
      component.settings = { kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', branchCode: '1110' };
      component.ngOnChanges();

      expect(component.seed()).toEqual({ clusterCode: 'CC', cbuCode: 'RCBU', branchCode: '1110', operationAreaCode: null });
    });

    it('writes only the canonical keys for a chosen location, replacing legacy ones', () => {
      component.settings = { kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', regionCode: 'JCBU', designerVersion: 2 };
      component.ngOnChanges();
      expect(component.conflict()).toBeTrue();

      component.chooseLocation({ clusterCode: 'CC', cbuCode: 'RCBU', branchCode: null, operationAreaCode: 'OA1' });

      expect(emitted.at(-1)).toEqual({ kind: 'Main', designerVersion: 2, clusterCode: 'CC', cbuCode: 'RCBU', operationAreaCode: 'OA1' });
      expect(component.conflict()).toBeFalse();
    });

    it('drops the dependent codes the shared selector cleared after a parent change', () => {
      component.settings = { kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', branchCode: '1110', operationAreaCode: 'OA1' };
      component.ngOnChanges();

      component.chooseLocation({ clusterCode: 'WC', cbuCode: null, branchCode: null, operationAreaCode: null });

      expect(emitted.at(-1)).toEqual({ kind: 'Main', clusterCode: 'WC' });
    });

    it('does not re-seed the selector from its own echo', () => {
      component.settings = { kind: 'Main', clusterCode: 'CC' };
      component.ngOnChanges();
      const seed = component.seed();

      component.chooseLocation({ clusterCode: 'CC', cbuCode: 'RCBU', branchCode: null, operationAreaCode: null });
      component.settings = emitted.at(-1)!;
      component.ngOnChanges();

      expect(component.seed()).toBe(seed);
    });

    it('preserves scope when switching to a child, upgrading legacy settings and keeping the designer version', () => {
      component.settings = { kind: 'Main', clusterCode: 'CC', cbuCode: 'RCBU', designerVersion: 2 };
      component.ngOnChanges();

      component.kind('Child');

      expect(emitted.at(-1)).toEqual({ kind: 'Child', designerVersion: 2, schemaVersion: 2,
        organizationScopes: [{ level: 'Cbu', code: 'RCBU', clusterCode: 'CC', cbuCode: 'RCBU' }] });
    });
  });
});
