import { describe, it, expect } from 'vitest';
import { DsmDatabase } from './db';

describe('Dexie Database Schema Version 5 Verification', () => {
  it('defines database schema version 5 with stationId and [stationId+pumpId] indexes', () => {
    const db = new DsmDatabase();
    expect(db.verno).toBe(5);

    const draftsTable = db.table('drafts');
    expect(draftsTable).toBeDefined();

    const draftsIndexNames = draftsTable.schema.indexes.map(idx => idx.name);
    expect(draftsIndexNames).toContain('stationId');
    expect(draftsIndexNames).toContain('[stationId+pumpId]');
    expect(draftsIndexNames).toContain('pumpId');
    expect(draftsIndexNames).toContain('status');
    expect(draftsIndexNames).toContain('shiftDate');

    const submissionsTable = db.table('submissions');
    expect(submissionsTable).toBeDefined();

    const submissionsIndexNames = submissionsTable.schema.indexes.map(idx => idx.name);
    expect(submissionsIndexNames).toContain('stationId');
    expect(submissionsIndexNames).toContain('[stationId+pumpId]');
    expect(submissionsIndexNames).toContain('pumpId');
    expect(submissionsIndexNames).toContain('status');
    expect(submissionsIndexNames).toContain('shiftDate');
  });

  it('preserves existing tables (products, stockBalances) across schema versions', () => {
    const db = new DsmDatabase();
    const tableNames = db.tables.map(t => t.name);

    expect(tableNames).toContain('drafts');
    expect(tableNames).toContain('submissions');
    expect(tableNames).toContain('products');
    expect(tableNames).toContain('stockBalances');
  });
});
