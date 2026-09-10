import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { QueryPerformance, QueryPlan } from '../../core/models/query.models';

export interface QueryPlanDialogData {
  query: QueryPerformance;
  plan: QueryPlan;
}

interface UsedPlanObject {
  database: string;
  schema: string;
  table: string;
  index: string;
}

@Component({
  selector: 'app-query-plan-dialog',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatDialogModule, MatIconModule],
  template: `
    <div class="dialog-shell">
      <div class="plan-head">
        <div>
          <div class="plan-title">
            <mat-icon>account_tree</mat-icon>
            <strong>Execution Plan</strong>
          </div>
          <span>Plan #{{ data.plan.planId }} · {{ data.plan.planHash }}</span>
        </div>
        <div class="plan-actions">
          <button mat-stroked-button type="button" (click)="copyPlan()">
            <mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
            {{ copied() ? 'Kopyalandı' : 'XML Kopyala' }}
          </button>
          <button mat-stroked-button type="button" (click)="downloadPlan()">
            <mat-icon>download</mat-icon>
            .sqlplan İndir
          </button>
          <button mat-button type="button" (click)="close()">
            <mat-icon>close</mat-icon>
            Kapat
          </button>
        </div>
      </div>

      <div class="context-grid">
        <div><span>Veritabanı</span><strong>{{ data.query.databaseName }}</strong></div>
        <div><span>Query Hash</span><strong class="mono">{{ data.query.queryHash }}</strong></div>
        <div><span>Plan Hash</span><strong class="mono">{{ data.plan.planHash }}</strong></div>
        <div><span>Kaynak</span><strong>{{ data.plan.source }}</strong></div>
      </div>

      <div class="sql-context">
        <span>Problemli SQL</span>
        <pre>{{ data.query.statementText }}</pre>
      </div>

      @if (usedObjects.length) {
        <div class="objects">
          <div class="section-title"><mat-icon>table_view</mat-icon><strong>Execution plan içindeki nesneler</strong></div>
          <div class="object-list">
            @for (item of usedObjects; track objectKey(item)) {
              <div class="object-row">
                <strong>{{ objectName(item) }}</strong>
                @if (item.index) { <span>İndeks: {{ item.index }}</span> }
              </div>
            }
          </div>
        </div>
      } @else {
        <div class="objects-empty">
          <mat-icon>info</mat-icon>
          <span>Plan XML içinde çözümlenebilir Object düğümü bulunamadı.</span>
        </div>
      }

      <div class="plan-meta">
        <span>İlk görüldü <strong>{{ data.plan.firstSeenAt | date:'dd.MM.yyyy HH:mm:ss' }}</strong></span>
        <span>Son görüldü <strong>{{ data.plan.lastSeenAt | date:'dd.MM.yyyy HH:mm:ss' }}</strong></span>
        <span>Runtime counters <strong>{{ data.plan.hasActualRuntimeCounters ? 'Var' : 'Yok' }}</strong></span>
      </div>

      <pre class="plan-xml">{{ data.plan.planXml }}</pre>
    </div>
  `,
  styles: [`
    :host{display:block;height:100%;min-height:0}.dialog-shell{height:100%;display:flex;flex-direction:column;min-height:0;padding:18px;box-sizing:border-box}.plan-head{display:flex;justify-content:space-between;gap:16px;align-items:center;border-bottom:1px solid #e7ebf1;padding-bottom:12px}.plan-title{display:flex;align-items:center;gap:7px}.plan-title mat-icon{color:#3157d5}.plan-head>div>span{display:block;color:#7c8799;font-family:monospace;font-size:.69rem;margin-top:4px;max-width:580px;overflow:hidden;text-overflow:ellipsis}.plan-actions{display:flex;gap:7px;flex-wrap:wrap}.context-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px;padding:12px 0}.context-grid div{background:#f7f9fc;border:1px solid #e8edf4;border-radius:9px;padding:9px 10px;min-width:0}.context-grid span,.sql-context>span{display:block;color:#7b8798;font-size:.64rem;margin-bottom:3px}.context-grid strong{display:block;color:#344054;font-size:.74rem;overflow:hidden;text-overflow:ellipsis}.mono{font-family:monospace}.sql-context{margin-bottom:10px}.sql-context pre{max-height:120px;overflow:auto;white-space:pre-wrap;background:#f7f9fc;border:1px solid #e8edf4;border-radius:9px;padding:10px;margin:0;color:#344054;font-size:.68rem;line-height:1.4}.objects{border:1px solid #dfe6f1;border-radius:10px;margin-bottom:10px;padding:10px}.section-title{display:flex;align-items:center;gap:6px;font-size:.72rem;margin-bottom:7px}.section-title mat-icon{font-size:17px;width:17px;height:17px;color:#3157d5}.object-list{display:flex;flex-wrap:wrap;gap:7px}.object-row{background:#f7f9fc;border:1px solid #e8edf4;border-radius:8px;padding:7px 9px}.object-row strong{display:block;font-size:.7rem}.object-row span{display:block;color:#778297;font-size:.62rem;margin-top:2px}.objects-empty{display:flex;align-items:center;gap:6px;color:#7b8798;font-size:.68rem;margin-bottom:10px}.objects-empty mat-icon{font-size:17px;width:17px;height:17px}.plan-meta{display:flex;gap:18px;flex-wrap:wrap;padding:0 2px 9px;color:#7b8798;font-size:.67rem}.plan-meta strong{color:#344054;margin-left:3px}.plan-xml{flex:1;min-height:180px;overflow:auto;margin:0;background:#0f172a;color:#dbe7f4;padding:15px;border-radius:10px;font-size:.72rem;line-height:1.45;white-space:pre;tab-size:2}@media(max-width:900px){.dialog-shell{padding:12px}.plan-head{align-items:flex-start;flex-direction:column}.plan-actions{width:100%}.context-grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
  `]
})
export class QueryPlanDialogComponent {
  readonly data = inject<QueryPlanDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<QueryPlanDialogComponent>);
  readonly copied = signal(false);
  readonly usedObjects = this.extractUsedObjects(this.data.plan.planXml);

  close(): void {
    this.dialogRef.close();
  }

  async copyPlan(): Promise<void> {
    await navigator.clipboard.writeText(this.data.plan.planXml);
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 1600);
  }

  downloadPlan(): void {
    const blob = new Blob([this.data.plan.planXml], { type: 'application/xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `query-${this.data.plan.queryId}-plan-${this.data.plan.planId}.sqlplan`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  objectName(item: UsedPlanObject): string {
    return [item.database, item.schema, item.table].filter(Boolean).join('.');
  }

  objectKey(item: UsedPlanObject): string {
    return `${item.database}|${item.schema}|${item.table}|${item.index}`;
  }

  private extractUsedObjects(planXml: string): UsedPlanObject[] {
    if (!planXml?.trim()) return [];

    try {
      const document = new DOMParser().parseFromString(planXml, 'application/xml');
      if (document.querySelector('parsererror')) return [];

      const unique = new Map<string, UsedPlanObject>();
      for (const node of Array.from(document.getElementsByTagName('Object'))) {
        const item: UsedPlanObject = {
          database: this.cleanIdentifier(node.getAttribute('Database')),
          schema: this.cleanIdentifier(node.getAttribute('Schema')),
          table: this.cleanIdentifier(node.getAttribute('Table')),
          index: this.cleanIdentifier(node.getAttribute('Index'))
        };

        if (!item.table) continue;
        unique.set(this.objectKey(item).toLocaleLowerCase(), item);
      }

      return Array.from(unique.values());
    } catch {
      return [];
    }
  }

  private cleanIdentifier(value: string | null): string {
    return (value ?? '').trim().replace(/^\[(.*)\]$/, '$1');
  }
}
