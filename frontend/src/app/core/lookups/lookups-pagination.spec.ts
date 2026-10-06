import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LookupItem, LookupsService } from './lookups.service';

describe('Complete lookup lists', () => {
  it('loads every page and preserves parent and active filters', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const rows: LookupItem[] = Array.from({ length: 501 }, (_, index) => ({ id: `${index}`, code: `${index}`, nameEn: `Branch ${index}`, nameAr: '', isActive: true, parentCode: 'CBU' }));
    let result: LookupItem[] = [];
    TestBed.inject(LookupsService).listAll('Branch', { parentCode: 'CBU', isActive: true }).subscribe(value => result = value);
    const first = http.expectOne(request => request.url === '/api/v1/lookups/branches' && request.params.get('pageNumber') === '1');
    first.flush({ isSuccess: true, value: { items: rows.slice(0, 500), totalCount: 501, pageNumber: 1, pageSize: 500 } });
    const second = http.expectOne(request => request.params.get('pageNumber') === '2' && request.params.get('parentCode') === 'CBU' && request.params.get('isActive') === 'true');
    second.flush({ isSuccess: true, value: { items: rows.slice(500), totalCount: 501, pageNumber: 2, pageSize: 500 } });
    expect(result).toEqual(rows);
    http.verify();
  });
});
