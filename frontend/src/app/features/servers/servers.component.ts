import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTableModule } from '@angular/material/table';
import { forkJoin } from 'rxjs';
import {
  CreateServerRequest,
  DatabaseOption,
  MonitoredTableSelection,
  ServerAuthenticationType,
  ServerListItem,
  TableOption
} from '../../core/models/server.models';
import { AdvisorApiService } from '../../core/services/advisor-api.service';

@Component({
  selector: 'app-servers',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatTableModule,
    MatProgressSpinnerModule
  ],
  template: `
    <h1 class="page-title">SQL Sunucuları</h1>
    <p class="page-subtitle">İzlenecek SQL Server bağlantılarını ve veritabanı / tablo kapsamını yönetin. Monitored bağlantılar yalnızca okuma ve performans izinleriyle kullanılmalıdır.</p>

    <div class="layout">
      <mat-card class="form-card">
        <mat-card-header><mat-card-title>Yeni SQL Server</mat-card-title></mat-card-header>
        <mat-card-content>
          <form [formGroup]="form" class="form-grid">
            <mat-form-field appearance="outline"><mat-label>Profil adı</mat-label><input matInput formControlName="name" placeholder="SQL-PROD"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Host / IP</mat-label><input matInput formControlName="host" placeholder="192.168.1.20"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Port</mat-label><input matInput type="number" formControlName="port"></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Başlangıç DB</mat-label><input matInput formControlName="defaultDatabase"></mat-form-field>
            <mat-form-field appearance="outline" class="wide">
              <mat-label>Kimlik doğrulama</mat-label>
              <mat-select formControlName="authenticationType">
                <mat-option [value]="auth.Windows">Windows Authentication</mat-option>
                <mat-option [value]="auth.SqlLogin">SQL Login</mat-option>
              </mat-select>
            </mat-form-field>
            @if (form.controls.authenticationType.value === auth.SqlLogin) {
              <mat-form-field appearance="outline"><mat-label>Kullanıcı</mat-label><input matInput formControlName="username" autocomplete="off"></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>Parola</mat-label><input matInput type="password" formControlName="password" autocomplete="new-password"></mat-form-field>
            }
            <div class="checks wide">
              <mat-checkbox formControlName="encrypt">Encrypt</mat-checkbox>
              <mat-checkbox formControlName="trustServerCertificate">Trust Server Certificate</mat-checkbox>
            </div>
          </form>
          @if (message()) { <div class="message" [class.error]="messageError()">{{ message() }}</div> }
          <div class="actions">
            <button mat-stroked-button type="button" (click)="test()" [disabled]="form.invalid || busy()">Bağlantıyı Test Et</button>
            <button mat-flat-button type="button" (click)="save()" [disabled]="form.invalid || busy()">Sunucuyu Ekle</button>
            @if (busy()) { <mat-spinner diameter="24" /> }
          </div>
        </mat-card-content>
      </mat-card>

      <div class="panel list-panel">
        <div class="list-title"><strong>İzlenen Sunucular</strong><span>{{ servers().length }} kayıt</span></div>
        @if (!servers().length) {
          <div class="no-data">Henüz sunucu eklenmedi.</div>
        } @else {
          <table mat-table [dataSource]="servers()">
            <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>Sunucu</th><td mat-cell *matCellDef="let s"><strong>{{ s.name }}</strong><small>{{ s.host }}:{{ s.port }}</small></td></ng-container>
            <ng-container matColumnDef="auth"><th mat-header-cell *matHeaderCellDef>Auth</th><td mat-cell *matCellDef="let s">{{ s.authenticationType === auth.Windows ? 'Windows' : 'SQL Login' }}</td></ng-container>
            <ng-container matColumnDef="last"><th mat-header-cell *matHeaderCellDef>Son Bağlantı</th><td mat-cell *matCellDef="let s">{{ s.lastConnectedAt ? (s.lastConnectedAt | date:'dd.MM.yyyy HH:mm:ss') : '—' }}</td></ng-container>
            <ng-container matColumnDef="scope">
              <th mat-header-cell *matHeaderCellDef>Tablo Kapsamı</th>
              <td mat-cell *matCellDef="let s"><button mat-stroked-button type="button" class="scope-button" (click)="openScope(s)"><mat-icon>table_view</mat-icon>Seç</button></td>
            </ng-container>
            <ng-container matColumnDef="enabled"><th mat-header-cell *matHeaderCellDef>Aktif</th><td mat-cell *matCellDef="let s"><mat-slide-toggle [checked]="s.isEnabled" (change)="toggle(s,$event.checked)"></mat-slide-toggle></td></ng-container>
            <tr mat-header-row *matHeaderRowDef="columns"></tr><tr mat-row *matRowDef="let row; columns: columns" [class.selected-row]="scopeServer()?.id === row.id"></tr>
          </table>
        }
      </div>
    </div>

    @if (scopeServer()) {
      <mat-card class="scope-card">
        <div class="scope-heading">
          <div>
            <div class="eyebrow">TABLO KAPSAMI</div>
            <h2>{{ scopeServer()!.name }}</h2>
            <p>Seçim yoksa bu sunucudaki tüm tablolar Index ve Statistics analizine girer. En az bir tablo seçildiğinde yalnız seçilen <b>veritabanı + şema + tablo</b> kombinasyonları izlenir.</p>
          </div>
          <div class="scope-state" [class.restricted]="selectedTables().length > 0">
            <mat-icon>{{ selectedTables().length > 0 ? 'filter_alt' : 'public' }}</mat-icon>
            <div><strong>{{ selectedTables().length > 0 ? selectedTables().length + ' tablo seçili' : 'Tüm tablolar' }}</strong><small>{{ selectedTables().length > 0 ? 'Whitelist aktif' : 'Kısıtlama yok' }}</small></div>
          </div>
        </div>

        @if (scopeMessage()) { <div class="message scope-message" [class.error]="scopeMessageError()">{{ scopeMessage() }}</div> }

        @if (scopeBusy() && !databases().length) {
          <div class="scope-loading"><mat-spinner diameter="34" /><span>Veritabanları ve mevcut kapsam yükleniyor…</span></div>
        } @else {
          <div class="scope-controls">
            <mat-form-field appearance="outline">
              <mat-label>Veritabanı</mat-label>
              <mat-select [value]="scopeDatabase()" (selectionChange)="changeDatabase($event.value)">
                @for (db of databases(); track db.name) { <mat-option [value]="db.name">{{ db.name }}</mat-option> }
              </mat-select>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>Tablo ara</mat-label>
              <mat-icon matPrefix>search</mat-icon>
              <input matInput [value]="tableSearch()" (input)="setTableSearch($event)" placeholder="dbo.PRO_WorkOrder">
            </mat-form-field>
            <div class="scope-actions">
              <button mat-stroked-button type="button" (click)="selectCurrentDatabase()" [disabled]="scopeBusy() || !tables().length">Bu DB'deki tümünü seç</button>
              <button mat-stroked-button type="button" (click)="clearCurrentDatabase()" [disabled]="scopeBusy() || !scopeDatabase()">Bu DB seçimini temizle</button>
            </div>
          </div>

          @if (scopeBusy()) {
            <div class="scope-loading compact"><mat-spinner diameter="28" /><span>Tablolar yükleniyor…</span></div>
          } @else if (!scopeDatabase()) {
            <div class="table-empty">Erişilebilir kullanıcı veritabanı bulunamadı.</div>
          } @else if (!tables().length) {
            <div class="table-empty">{{ scopeDatabase() }} içinde erişilebilir kullanıcı tablosu bulunamadı.</div>
          } @else {
            <div class="table-list-head"><strong>{{ scopeDatabase() }}</strong><span>{{ visibleTables().length }} / {{ tables().length }} tablo</span></div>
            <div class="table-list">
              @for (table of visibleTables(); track table.schemaName + '.' + table.tableName) {
                <label class="table-item" [class.checked]="isSelected(table)">
                  <mat-checkbox [checked]="isSelected(table)" (change)="toggleTable(table, $event.checked)"></mat-checkbox>
                  <div><strong>{{ table.schemaName }}.{{ table.tableName }}</strong><small>{{ table.databaseName }}</small></div>
                </label>
              }
            </div>
          }

          <div class="selected-summary">
            <div><strong>Seçili kapsam</strong><span>{{ selectedTables().length ? selectedTables().length + ' tablo' : 'Tüm tablolar izlenecek' }}</span></div>
            @if (selectedTables().length) {
              <div class="db-badges">@for (row of selectedDatabaseCounts(); track row.databaseName) { <span>{{ row.databaseName }} · {{ row.count }}</span> }</div>
            }
          </div>

          <div class="save-scope">
            <button mat-stroked-button type="button" (click)="clearAllScope()" [disabled]="scopeBusy() || !selectedTables().length"><mat-icon>filter_alt_off</mat-icon>Kısıtlamayı Kaldır</button>
            <button mat-flat-button type="button" (click)="saveScope()" [disabled]="scopeBusy()"><mat-icon>save</mat-icon>Kapsamı Kaydet</button>
          </div>
        }
      </mat-card>
    }
  `,
  styles: [`
    .layout{display:grid;grid-template-columns:440px 1fr;gap:20px;align-items:start}.form-card,.list-panel,.scope-card{border:1px solid #e3e7ef;border-radius:14px;box-shadow:0 3px 18px rgba(20,32,55,.05)}.form-card mat-card-header{padding:20px 20px 6px}.form-card mat-card-content{padding:16px 20px 20px}.form-grid{display:grid;grid-template-columns:1fr 1fr;gap:2px 12px}.wide{grid-column:1/-1}.checks{display:flex;gap:18px;margin:0 0 16px}.actions{display:flex;gap:10px;align-items:center}.actions button[mat-flat-button],.save-scope button[mat-flat-button]{background:#244fc5;color:#fff}.message{margin:4px 0 14px;padding:10px 12px;border-radius:8px;background:#eaf7f0;color:#146b48;font-size:.82rem}.message.error{background:#ffeded;color:#a12e2e}
    .list-panel{overflow:hidden}.list-title{display:flex;justify-content:space-between;padding:18px;border-bottom:1px solid #e8ebf1}.list-title span{color:#748095;font-size:.8rem}.no-data{padding:50px;text-align:center;color:#7a8597}table{width:100%}td small{display:block;color:#818b9c;margin-top:3px}th{color:#667085;font-size:.72rem}.scope-button{height:34px;font-size:.72rem}.scope-button mat-icon{font-size:17px;width:17px;height:17px;margin-right:4px}.selected-row{background:#f4f7ff}
    .scope-card{margin-top:20px;padding:20px}.scope-heading{display:flex;justify-content:space-between;gap:24px;align-items:flex-start;border-bottom:1px solid #edf0f5;padding-bottom:16px}.eyebrow{font-size:.6rem;font-weight:800;letter-spacing:.12em;color:#3157c8}.scope-heading h2{margin:4px 0 5px;font-size:1.05rem}.scope-heading p{margin:0;max-width:880px;color:#6f7b8e;font-size:.74rem;line-height:1.55}.scope-state{display:flex;gap:8px;align-items:center;min-width:160px;background:#eef8f3;color:#226b4d;border:1px solid #d8ebe1;padding:10px 12px;border-radius:11px}.scope-state.restricted{background:#eef3ff;color:#3157a4;border-color:#d8e1f8}.scope-state mat-icon{font-size:20px;width:20px;height:20px}.scope-state strong,.scope-state small{display:block}.scope-state strong{font-size:.75rem}.scope-state small{font-size:.6rem;margin-top:2px;opacity:.75}.scope-message{margin:14px 0 0}.scope-controls{display:grid;grid-template-columns:260px minmax(260px,1fr) auto;gap:12px;align-items:center;margin-top:18px}.scope-controls mat-form-field{width:100%}.scope-actions{display:flex;gap:8px;align-items:center;margin-top:-20px}.scope-actions button{font-size:.68rem}.scope-loading{min-height:180px;display:flex;justify-content:center;align-items:center;gap:12px;color:#768297}.scope-loading.compact{min-height:100px}.table-empty{padding:40px;text-align:center;border:1px dashed #d8dee8;border-radius:10px;color:#7e8999}.table-list-head{display:flex;justify-content:space-between;margin:2px 0 8px;font-size:.72rem}.table-list-head span{color:#7b8798}.table-list{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:7px;max-height:430px;overflow:auto;padding:3px}.table-item{display:flex;align-items:center;gap:7px;border:1px solid #e4e8ef;border-radius:9px;padding:7px 9px;cursor:pointer;background:#fff}.table-item.checked{border-color:#b9c9f4;background:#f5f8ff}.table-item strong,.table-item small{display:block}.table-item strong{font-size:.7rem;word-break:break-word}.table-item small{font-size:.58rem;color:#8b95a5;margin-top:2px}.selected-summary{display:flex;justify-content:space-between;gap:14px;align-items:center;margin-top:16px;padding:12px 13px;background:#f7f9fc;border-radius:10px}.selected-summary strong,.selected-summary>div>span{display:block}.selected-summary strong{font-size:.7rem}.selected-summary>div>span{font-size:.64rem;color:#7d8898;margin-top:2px}.db-badges{display:flex;gap:5px;flex-wrap:wrap;justify-content:flex-end}.db-badges span{font-size:.59rem!important;margin:0!important;color:#536279!important;background:#fff;border:1px solid #e1e6ee;padding:4px 7px;border-radius:999px}.save-scope{display:flex;justify-content:flex-end;gap:9px;margin-top:14px}.save-scope mat-icon{font-size:17px;width:17px;height:17px}
    @media(max-width:1100px){.scope-controls{grid-template-columns:1fr 1fr}.scope-actions{grid-column:1/-1;margin-top:-12px}.scope-heading{display:block}.scope-state{margin-top:12px;width:max-content}}@media(max-width:1000px){.layout{grid-template-columns:1fr}}@media(max-width:650px){.form-grid,.scope-controls{grid-template-columns:1fr}.wide{grid-column:auto}.scope-actions{grid-column:auto;display:grid}.table-list{grid-template-columns:1fr}.selected-summary{display:block}.db-badges{justify-content:flex-start;margin-top:8px}.save-scope{display:grid}.scope-card{padding:14px}}
  `]
})
export class ServersComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(AdvisorApiService);

  readonly auth = ServerAuthenticationType;
  readonly servers = signal<ServerListItem[]>([]);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly messageError = signal(false);
  readonly columns = ['name', 'auth', 'last', 'scope', 'enabled'];

  readonly scopeServer = signal<ServerListItem | null>(null);
  readonly databases = signal<DatabaseOption[]>([]);
  readonly scopeDatabase = signal('');
  readonly tables = signal<TableOption[]>([]);
  readonly selectedTables = signal<MonitoredTableSelection[]>([]);
  readonly tableSearch = signal('');
  readonly scopeBusy = signal(false);
  readonly scopeMessage = signal('');
  readonly scopeMessageError = signal(false);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    host: ['', Validators.required],
    port: [1433, [Validators.required, Validators.min(1), Validators.max(65535)]],
    defaultDatabase: ['master', Validators.required],
    authenticationType: [ServerAuthenticationType.Windows, Validators.required],
    username: [''],
    password: [''],
    encrypt: [true],
    trustServerCertificate: [true]
  });

  ngOnInit(): void { this.load(); }

  test(): void {
    this.busy.set(true); this.message.set('');
    this.api.testServer(this.toRequest()).subscribe({
      next: r => { this.busy.set(false); this.messageError.set(false); this.message.set(`Bağlantı başarılı: ${r.serverName} · ${r.productVersion} · ${r.edition}`); },
      error: e => { this.busy.set(false); this.messageError.set(true); this.message.set(e?.error?.error ?? e?.error ?? 'Bağlantı testi başarısız.'); }
    });
  }

  save(): void {
    this.busy.set(true); this.message.set('');
    this.api.createServer(this.toRequest()).subscribe({
      next: () => { this.busy.set(false); this.messageError.set(false); this.message.set('Sunucu eklendi. Worker en geç 15 saniye içinde snapshot toplamaya başlayacak.'); this.form.patchValue({name:'', host:'', username:'', password:''}); this.load(); },
      error: e => { this.busy.set(false); this.messageError.set(true); this.message.set(e?.error?.error ?? e?.error ?? 'Sunucu eklenemedi.'); }
    });
  }

  toggle(server: ServerListItem, enabled: boolean): void {
    this.api.setServerEnabled(server.id, enabled).subscribe({ next: () => this.load() });
  }

  openScope(server: ServerListItem): void {
    this.scopeServer.set(server);
    this.scopeBusy.set(true);
    this.scopeMessage.set('');
    this.scopeMessageError.set(false);
    this.databases.set([]);
    this.tables.set([]);
    this.scopeDatabase.set('');
    this.tableSearch.set('');

    forkJoin({
      databases: this.api.getServerDatabases(server.id),
      scope: this.api.getTableScope(server.id)
    }).subscribe({
      next: result => {
        this.databases.set(result.databases);
        this.selectedTables.set(result.scope.tables);
        const firstDatabase = result.scope.tables[0]?.databaseName ?? result.databases[0]?.name ?? '';
        this.scopeDatabase.set(firstDatabase);
        if (firstDatabase) this.loadTables(firstDatabase);
        else this.scopeBusy.set(false);
      },
      error: e => {
        this.scopeBusy.set(false);
        this.scopeMessageError.set(true);
        this.scopeMessage.set(this.errorText(e, 'Tablo kapsamı yüklenemedi.'));
      }
    });
  }

  changeDatabase(databaseName: string): void {
    this.scopeDatabase.set(databaseName);
    this.tableSearch.set('');
    this.loadTables(databaseName);
  }

  setTableSearch(event: Event): void {
    this.tableSearch.set((event.target as HTMLInputElement).value ?? '');
  }

  visibleTables(): TableOption[] {
    const q = this.tableSearch().trim().toLocaleLowerCase('tr-TR');
    if (!q) return this.tables();
    return this.tables().filter(x =>
      `${x.schemaName}.${x.tableName}`.toLocaleLowerCase('tr-TR').includes(q) ||
      x.tableName.toLocaleLowerCase('tr-TR').includes(q));
  }

  isSelected(table: TableOption): boolean {
    const key = this.tableKey(table.databaseName, table.schemaName, table.tableName);
    return this.selectedTables().some(x => this.tableKey(x.databaseName, x.schemaName, x.tableName) === key);
  }

  toggleTable(table: TableOption, checked: boolean): void {
    const key = this.tableKey(table.databaseName, table.schemaName, table.tableName);
    const next = this.selectedTables().filter(x => this.tableKey(x.databaseName, x.schemaName, x.tableName) !== key);
    if (checked) next.push({ databaseName: table.databaseName, schemaName: table.schemaName, tableName: table.tableName });
    this.selectedTables.set(this.sortSelections(next));
  }

  selectCurrentDatabase(): void {
    const databaseName = this.scopeDatabase();
    if (!databaseName) return;
    const otherDatabases = this.selectedTables().filter(x => x.databaseName.toLowerCase() !== databaseName.toLowerCase());
    const current = this.tables().map(x => ({ databaseName: x.databaseName, schemaName: x.schemaName, tableName: x.tableName }));
    this.selectedTables.set(this.sortSelections([...otherDatabases, ...current]));
  }

  clearCurrentDatabase(): void {
    const databaseName = this.scopeDatabase();
    this.selectedTables.set(this.selectedTables().filter(x => x.databaseName.toLowerCase() !== databaseName.toLowerCase()));
  }

  selectedDatabaseCounts(): { databaseName: string; count: number }[] {
    const counts = new Map<string, { databaseName: string; count: number }>();
    for (const table of this.selectedTables()) {
      const key = table.databaseName.toLowerCase();
      const current = counts.get(key) ?? { databaseName: table.databaseName, count: 0 };
      current.count++;
      counts.set(key, current);
    }
    return [...counts.values()].sort((a, b) => a.databaseName.localeCompare(b.databaseName));
  }

  saveScope(): void {
    const server = this.scopeServer();
    if (!server) return;
    this.scopeBusy.set(true);
    this.scopeMessage.set('');
    this.api.updateTableScope(server.id, { tables: this.selectedTables() }).subscribe({
      next: result => {
        this.scopeBusy.set(false);
        this.selectedTables.set(result.tables);
        this.scopeMessageError.set(false);
        this.scopeMessage.set(result.restricted
          ? `Kapsam kaydedildi: yalnız ${result.tables.length} tablo Index ve Statistics analizinde gösterilecek ve toplanacak.`
          : 'Kısıtlama kaldırıldı: tüm tablolar yeniden izlenecek.');
      },
      error: e => {
        this.scopeBusy.set(false);
        this.scopeMessageError.set(true);
        this.scopeMessage.set(this.errorText(e, 'Tablo kapsamı kaydedilemedi.'));
      }
    });
  }

  clearAllScope(): void {
    const server = this.scopeServer();
    if (!server) return;
    this.scopeBusy.set(true);
    this.scopeMessage.set('');
    this.api.updateTableScope(server.id, { tables: [] }).subscribe({
      next: () => {
        this.scopeBusy.set(false);
        this.selectedTables.set([]);
        this.scopeMessageError.set(false);
        this.scopeMessage.set('Kısıtlama kaldırıldı: bu sunucudaki tüm tablolar yeniden izlenecek.');
      },
      error: e => {
        this.scopeBusy.set(false);
        this.scopeMessageError.set(true);
        this.scopeMessage.set(this.errorText(e, 'Kısıtlama kaldırılamadı.'));
      }
    });
  }

  private loadTables(databaseName: string): void {
    const server = this.scopeServer();
    if (!server || !databaseName) { this.scopeBusy.set(false); return; }
    this.scopeBusy.set(true);
    this.tables.set([]);
    this.api.getServerTables(server.id, databaseName).subscribe({
      next: rows => { this.tables.set(rows); this.scopeBusy.set(false); },
      error: e => {
        this.scopeBusy.set(false);
        this.scopeMessageError.set(true);
        this.scopeMessage.set(this.errorText(e, `${databaseName} tablo listesi alınamadı.`));
      }
    });
  }

  private load(): void {
    this.api.getServers().subscribe({ next: x => this.servers.set(x) });
  }

  private toRequest(): CreateServerRequest {
    const v = this.form.getRawValue();
    return { ...v, username: v.username || null, password: v.password || null };
  }

  private sortSelections(rows: MonitoredTableSelection[]): MonitoredTableSelection[] {
    const unique = new Map<string, MonitoredTableSelection>();
    for (const row of rows) unique.set(this.tableKey(row.databaseName, row.schemaName, row.tableName), row);
    return [...unique.values()].sort((a, b) =>
      a.databaseName.localeCompare(b.databaseName) ||
      a.schemaName.localeCompare(b.schemaName) ||
      a.tableName.localeCompare(b.tableName));
  }

  private tableKey(databaseName: string, schemaName: string, tableName: string): string {
    return `${databaseName}\u001f${schemaName}\u001f${tableName}`.toLowerCase();
  }

  private errorText(error: any, fallback: string): string {
    return error?.error?.error ?? (typeof error?.error === 'string' ? error.error : fallback);
  }
}
