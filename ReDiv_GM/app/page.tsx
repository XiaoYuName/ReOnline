'use client';

import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';

const API_BASE = process.env.NEXT_PUBLIC_GM_API ?? 'http://127.0.0.1:5168/api';

type Account = {
  accountId: number;
  username: string;
  characterSlots: number;
  createdAtMicros: number;
  lastLoginAtMicros: number | null;
  characterCount: number;
  onlineSessions: number;
};

type Character = {
  characterId: number;
  accountId: number;
  name: string;
  jobId: number;
  level: number;
  exp: number;
  star: number;
  createdAtMicros: number;
  lastPlayedAtMicros: number | null;
  deleted: boolean;
};

type WorldTime = { bandId: number; overrideBandId: number; changedAtMicros: number };
type Dashboard = {
  accounts: Account[];
  characters: Character[];
  sessions: unknown[];
  worldTime: WorldTime;
  stats: { accounts: number; activeCharacters: number; deletedCharacters: number; onlineSessions: number };
  refreshedAtUnixMs: number;
};

type ServerLog = {
  level: string;
  timestampMicros: number;
  target: string;
  filename: string | null;
  lineNumber: number | null;
  function: string | null;
  message: string;
};

const bandNames: Record<number, string> = { 1: '早晨', 2: '中午', 3: '夜晚' };
const bandRanges: Record<number, string> = { 1: '05:00—11:00', 2: '11:00—18:00', 3: '18:00—05:00' };

async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, { cache: 'no-store', ...options });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: string } | null;
    throw new Error(body?.error ?? `请求失败（HTTP ${response.status}）`);
  }
  return response.json() as Promise<T>;
}

function writeOptions(method: 'POST' | 'PATCH', body: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json', 'X-ReDiv-GM': '1' },
    body: JSON.stringify(body),
  };
}

function formatTime(micros?: number | null, includeDate = true) {
  if (!micros) return '从未';
  return new Intl.DateTimeFormat('zh-CN', {
    ...(includeDate ? { month: '2-digit', day: '2-digit' } : {}),
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  }).format(new Date(micros / 1000));
}

export default function Home() {
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [logs, setLogs] = useState<ServerLog[]>([]);
  const [search, setSearch] = useState('');
  const [logLevel, setLogLevel] = useState('');
  const [logsPaused, setLogsPaused] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [accountEditor, setAccountEditor] = useState<Account | null>(null);
  const [characterEditor, setCharacterEditor] = useState<Character | null>(null);

  const refreshDashboard = useCallback(async (quiet = false) => {
    if (!quiet) setLoading(true);
    try {
      setDashboard(await api<Dashboard>('/dashboard'));
      setError('');
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '无法连接本地 GM API');
    } finally {
      if (!quiet) setLoading(false);
    }
  }, []);

  const refreshLogs = useCallback(async () => {
    if (logsPaused) return;
    try {
      const query = new URLSearchParams({ lines: '180' });
      if (logLevel) query.set('level', logLevel);
      setLogs(await api<ServerLog[]>(`/logs?${query}`));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '日志读取失败');
    }
  }, [logLevel, logsPaused]);

  useEffect(() => {
    const initial = window.setTimeout(() => void refreshDashboard(), 0);
    const timer = window.setInterval(() => void refreshDashboard(true), 10_000);
    return () => {
      window.clearTimeout(initial);
      window.clearInterval(timer);
    };
  }, [refreshDashboard]);

  useEffect(() => {
    const initial = window.setTimeout(() => void refreshLogs(), 0);
    const timer = window.setInterval(() => void refreshLogs(), 5_000);
    return () => {
      window.clearTimeout(initial);
      window.clearInterval(timer);
    };
  }, [refreshLogs]);

  const filteredAccounts = useMemo(() => {
    const needle = search.trim().toLowerCase();
    if (!needle) return dashboard?.accounts ?? [];
    const matchingAccountIds = new Set((dashboard?.characters ?? [])
      .filter(character => character.name.toLowerCase().includes(needle))
      .map(character => character.accountId));
    return (dashboard?.accounts ?? []).filter(account =>
      account.username.toLowerCase().includes(needle) ||
      String(account.accountId).includes(needle) || matchingAccountIds.has(account.accountId));
  }, [dashboard, search]);

  const filteredCharacters = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return (dashboard?.characters ?? []).filter(character => {
      const owner = dashboard?.accounts.find(account => account.accountId === character.accountId)?.username ?? '';
      return !needle || character.name.toLowerCase().includes(needle) || owner.toLowerCase().includes(needle) ||
        String(character.characterId).includes(needle);
    });
  }, [dashboard, search]);

  async function setWorldTime(overrideBandId: number) {
    const label = overrideBandId === 0 ? '恢复自动时间' : `锁定为${bandNames[overrideBandId]}`;
    if (!window.confirm(`确认${label}？这会立即影响所有在线玩家的城镇背景。`)) return;
    setBusy(true);
    try {
      await api<WorldTime>('/world-time', writeOptions('POST', { overrideBandId }));
      setNotice(`${label}已生效`);
      await refreshDashboard(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '时段修改失败');
    } finally {
      setBusy(false);
    }
  }

  async function saveAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!accountEditor) return;
    const data = new FormData(event.currentTarget);
    const characterSlots = Number(data.get('characterSlots'));
    if (!window.confirm(`确认把账号 ${accountEditor.username} 的角色栏位改为 ${characterSlots}？`)) return;
    setBusy(true);
    try {
      await api<Account>(`/accounts/${accountEditor.accountId}/slots`, writeOptions('PATCH', { characterSlots }));
      setAccountEditor(null);
      setNotice(`账号 ${accountEditor.username} 已更新`);
      await refreshDashboard(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '账号修改失败');
    } finally {
      setBusy(false);
    }
  }

  async function saveCharacter(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!characterEditor) return;
    const data = new FormData(event.currentTarget);
    const body = { level: Number(data.get('level')), exp: Number(data.get('exp')), star: Number(data.get('star')) };
    if (!window.confirm(`确认修改角色 ${characterEditor.name}（#${characterEditor.characterId}）？`)) return;
    setBusy(true);
    try {
      await api<Character>(`/characters/${characterEditor.characterId}`, writeOptions('PATCH', body));
      setCharacterEditor(null);
      setNotice(`角色 ${characterEditor.name} 已更新；在线角色建议重新选角以刷新公开投影`);
      await refreshDashboard(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : '角色修改失败');
    } finally {
      setBusy(false);
    }
  }

  const currentBand = dashboard?.worldTime.bandId ?? 0;
  const overrideBand = dashboard?.worldTime.overrideBandId ?? 0;
  const stats = dashboard?.stats;

  return (
    <main className="shell">
      <aside className="rail">
        <div className="brandMark">R</div>
        <nav aria-label="GM 工具导航">
          <a className="railItem active" href="#overview" title="总览">总</a>
          <a className="railItem" href="#accounts" title="账号">账</a>
          <a className="railItem" href="#characters" title="角色">角</a>
          <a className="railItem" href="#logs" title="日志">志</a>
        </nav>
        <div className={`railStatus ${error ? 'down' : ''}`} title={error ? 'GM API 异常' : '服务器连接正常'} />
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div><p className="eyebrow">REDIV / LOCAL OPERATIONS</p><h1>游戏服务器控制台</h1></div>
          <div className={`serverPill ${error ? 'disconnected' : ''}`}>
            <span className="pulse" />
            <span><strong>{error ? '连接异常' : 'rediv'}</strong><small>SpacetimeDB · 127.0.0.1:2383</small></span>
          </div>
        </header>

        <div className="content" id="overview">
          {error && <div className="message errorMessage"><strong>连接或操作失败</strong><span>{error}</span><button onClick={() => void refreshDashboard()} type="button">重试</button></div>}
          {notice && <div className="message noticeMessage"><span>{notice}</span><button onClick={() => setNotice('')} type="button" aria-label="关闭提示">×</button></div>}

          <section className="statGrid" aria-label="服务器摘要">
            <article className="statCard cyan"><span>账号总数</span><strong>{loading ? '—' : stats?.accounts ?? 0}</strong><small>凭据字段始终隐藏</small></article>
            <article className="statCard violet"><span>存活角色</span><strong>{loading ? '—' : stats?.activeCharacters ?? 0}</strong><small>{stats?.deletedCharacters ?? 0} 个已软删</small></article>
            <article className="statCard green"><span>在线连接</span><strong>{loading ? '—' : stats?.onlineSessions ?? 0}</strong><small>当前登录会话</small></article>
            <article className="statCard amber"><span>服务器时段</span><strong>{bandNames[currentBand] ?? '—'}</strong><small>{overrideBand === 0 ? '自动模式' : 'GM 锁定'} · Band {currentBand || '—'}</small></article>
          </section>

          <section className="controlGrid">
            <article className="panel timePanel">
              <div className="panelHeading">
                <div><p className="eyebrow">WORLD TIME</p><h2>世界时段控制</h2></div>
                <span className="modeBadge">{overrideBand === 0 ? '自动' : 'GM 锁定'}</span>
              </div>
              <div className={`timeDial band${currentBand}`} aria-label={`当前为${bandNames[currentBand] ?? '未知'}时段`}>
                <span className={`arc morning ${currentBand === 1 ? 'active' : ''}`}>早</span>
                <span className={`arc noon ${currentBand === 2 ? 'active' : ''}`}>中</span>
                <span className={`arc night ${currentBand === 3 ? 'active' : ''}`}>晚</span>
                <div className="dialCenter"><strong>{bandNames[currentBand] ?? '读取中'}</strong><small>{bandRanges[currentBand] ?? '—'}</small></div>
              </div>
              <div className="timeActions">
                {[0, 1, 2, 3].map(value => <button className={overrideBand === value ? 'selected' : ''} disabled={busy} key={value} onClick={() => void setWorldTime(value)} type="button">{value === 0 ? '恢复自动' : `锁定${bandNames[value]}`}</button>)}
              </div>
              <p className="safeNote">锁定会持续覆盖定时计算；恢复自动后按服务器 UTC+8 与配置边界计算。</p>
            </article>

            <article className="panel activityPanel">
              <div className="panelHeading">
                <div><p className="eyebrow">LIVE ACTIVITY</p><h2>实时概况</h2></div>
                <button className="ghostButton" disabled={loading} onClick={() => void refreshDashboard()} type="button">立即刷新</button>
              </div>
              <div className="activityRows">
                <div><span>数据库</span><strong className={error ? 'bad' : 'ok'}>{error ? '不可用' : '运行中'}</strong></div>
                <div><span>世界时间定时器</span><strong className="ok">每 60 秒</strong></div>
                <div><span>最后同步</span><strong>{dashboard ? new Date(dashboard.refreshedAtUnixMs).toLocaleTimeString('zh-CN', { hour12: false }) : '—'}</strong></div>
                <div><span>本地 GM API</span><strong className={error ? 'bad' : 'ok'}>{error ? '未连接' : '已连接'}</strong></div>
              </div>
              <div className="warningBox"><strong>本机管理模式</strong><p>只监听 127.0.0.1；写操作使用数据库 owner 身份并记录到本地 JSONL 审计日志。</p></div>
            </article>
          </section>

          <section className="panel tablePanel" id="accounts">
            <div className="panelHeading">
              <div><p className="eyebrow">PLAYER DIRECTORY</p><h2>账号</h2></div>
              <label className="search"><span>⌕</span><input aria-label="搜索账号或角色" onChange={event => setSearch(event.target.value)} placeholder="搜索账号、角色或 ID…" value={search} /></label>
            </div>
            <div className="tableWrap"><table>
              <thead><tr><th>账号</th><th>ID</th><th>角色栏位</th><th>存活角色</th><th>状态</th><th>最后登录</th><th /></tr></thead>
              <tbody>{filteredAccounts.map(account => <tr key={account.accountId}>
                <td><span className="avatar">{account.username.slice(0, 1).toUpperCase()}</span><strong>{account.username}</strong></td>
                <td className="mono">#{account.accountId}</td><td>{account.characterSlots} / 8</td><td>{account.characterCount}</td>
                <td><span className={account.onlineSessions ? 'onlineTag' : 'offlineTag'}>{account.onlineSessions ? `在线 ×${account.onlineSessions}` : '离线'}</span></td>
                <td>{formatTime(account.lastLoginAtMicros)}</td><td><button className="rowButton" onClick={() => setAccountEditor(account)} type="button">管理栏位</button></td>
              </tr>)}</tbody>
            </table>{!filteredAccounts.length && <p className="emptyState">没有匹配的账号</p>}</div>
          </section>

          <section className="panel tablePanel" id="characters">
            <div className="panelHeading"><div><p className="eyebrow">CHARACTER DATA</p><h2>角色数据</h2></div><span className="mutedLabel">支持等级 / 经验 / 星级</span></div>
            <div className="tableWrap"><table>
              <thead><tr><th>角色</th><th>ID / 账号</th><th>职业</th><th>等级</th><th>经验</th><th>星级</th><th>状态</th><th /></tr></thead>
              <tbody>{filteredCharacters.map(character => {
                const owner = dashboard?.accounts.find(account => account.accountId === character.accountId);
                return <tr className={character.deleted ? 'deletedRow' : ''} key={character.characterId}>
                  <td><span className="avatar characterAvatar">{character.name.slice(0, 1)}</span><strong>{character.name}</strong></td>
                  <td><span className="mono">#{character.characterId}</span><small>{owner?.username ?? `账号 #${character.accountId}`}</small></td>
                  <td>Job {character.jobId}</td><td>Lv. {character.level}</td><td className="mono">{character.exp.toLocaleString()}</td><td>{'★'.repeat(character.star)}</td>
                  <td><span className={character.deleted ? 'deletedTag' : 'onlineTag'}>{character.deleted ? '已软删' : '有效'}</span></td>
                  <td><button className="rowButton" disabled={character.deleted} onClick={() => setCharacterEditor(character)} type="button">修改</button></td>
                </tr>;
              })}</tbody>
            </table>{!filteredCharacters.length && <p className="emptyState">没有匹配的角色</p>}</div>
            <p className="tableNote">直接修改在线角色后，角色选中公开投影可能仍是旧值；让玩家重新选角或重连即可刷新。</p>
          </section>

          <section className="panel logPanel" id="logs">
            <div className="panelHeading">
              <div><p className="eyebrow">SERVER STREAM</p><h2>服务器日志</h2></div>
              <div className="logTools"><select aria-label="日志等级" onChange={event => setLogLevel(event.target.value)} value={logLevel}><option value="">全部等级</option><option value="debug">Debug</option><option value="info">Info</option><option value="warn">Warn</option><option value="error">Error</option></select><button className="ghostButton" onClick={() => setLogsPaused(value => !value)} type="button">{logsPaused ? '继续刷新' : '暂停刷新'}</button></div>
            </div>
            <div className="terminal" aria-live="polite">{logs.map((log, index) => <div className="logLine" key={`${log.timestampMicros}-${index}`}>
              <span className={`logLevel ${log.level.toLowerCase()}`}>{log.level.toUpperCase()}</span><time>{formatTime(log.timestampMicros, false)}</time><span className="logTarget" title={log.filename ?? ''}>{log.target || log.function || 'Module'}</span><p>{log.message}</p>
            </div>)}{!logs.length && <p className="emptyState">{logsPaused ? '日志刷新已暂停' : '暂无日志'}</p>}</div>
          </section>
        </div>
      </section>

      {accountEditor && <div className="modalBackdrop" onMouseDown={() => setAccountEditor(null)}>
        <form className="editorCard" onMouseDown={event => event.stopPropagation()} onSubmit={saveAccount}>
          <div><p className="eyebrow">ACCOUNT #{accountEditor.accountId}</p><h2>管理 {accountEditor.username}</h2></div>
          <label>角色栏位（1—8）<input defaultValue={accountEditor.characterSlots} max="8" min="1" name="characterSlots" required type="number" /></label>
          <p className="editorHint">密码哈希与盐不会通过 GM API 返回，也不能在此修改。</p>
          <div className="editorActions"><button onClick={() => setAccountEditor(null)} type="button">取消</button><button className="primaryButton" disabled={busy} type="submit">{busy ? '保存中…' : '确认保存'}</button></div>
        </form>
      </div>}

      {characterEditor && <div className="modalBackdrop" onMouseDown={() => setCharacterEditor(null)}>
        <form className="editorCard" onMouseDown={event => event.stopPropagation()} onSubmit={saveCharacter}>
          <div><p className="eyebrow">CHARACTER #{characterEditor.characterId}</p><h2>修改 {characterEditor.name}</h2></div>
          <div className="fieldGrid"><label>等级<input defaultValue={characterEditor.level} max="999" min="1" name="level" required type="number" /></label><label>星级<input defaultValue={characterEditor.star} max="6" min="1" name="star" required type="number" /></label></div>
          <label>经验值<input defaultValue={characterEditor.exp} min="0" name="exp" required type="number" /></label>
          <p className="editorHint">星级会影响当前形态的配置计算，但不会绕过项目中尚未设计的养成来源。</p>
          <div className="editorActions"><button onClick={() => setCharacterEditor(null)} type="button">取消</button><button className="primaryButton" disabled={busy} type="submit">{busy ? '保存中…' : '确认保存'}</button></div>
        </form>
      </div>}
    </main>
  );
}
