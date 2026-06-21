/* ============================================================
   am-views.jsx — OrchestratorView, HistoryView, ScheduledView
   ============================================================ */
const { Icon: VIcon, AGENTS: VAGENTS, AGENT_LIST: VLIST, PROJECT: VPROJECT } = window.AM;

/* ---------- shared ---------- */
function diffStat(s){
  const files = s.files || [];
  return { add: files.reduce((a,f)=>a+f.add,0), del: files.reduce((a,f)=>a+f.del,0), n: files.length };
}
function timeStr(ms){
  const d = new Date(ms);
  const p = n => String(n).padStart(2,'0');
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

/* ============================================================
   ORCHESTRATOR
   ============================================================ */
function OrchCard({ s, tokens, onOpen, onReview }){
  const live = s.status==='running' || s.status==='waiting';
  useTick(live);
  const a = VAGENTS[s.agentId];
  const ds = diffStat(s);
  const elapsed = fmtElapsed(Date.now()-s.startedAt);
  const total = Math.max(1, ds.add + ds.del);
  const actCls = s.status==='running'?'run':s.status==='waiting'?'wait':s.status==='error'?'err':s.status==='done'?'done':'';
  const actIcon = s.status==='waiting'?'warn':s.status==='error'?'alert':s.status==='done'?'check':null;

  return (
    <div className={`ocard ${live?'live':''} ${(s.status==='done'||s.status==='error')?'dim':''}`} onClick={()=>onOpen(s.id)}>
      <div className="ocard-head">
        <div className="ocard-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
        <div className="ocard-id">
          <div className="ocard-ag">{a.name}</div>
          <div className="ocard-model">{s.model}</div>
        </div>
        <div className="ocard-st" style={{ color: STATUS_COLOR[s.status] }}>
          <StatusDot status={s.status} /> {STATUS_LABEL[s.status]}
        </div>
      </div>
      <div className="ocard-body">
        <div className="ocard-task">{s.title}</div>
        <div className="ocard-meta">
          <span className="br"><VIcon name="branch" size={11} /><span>{s.branch}</span></span>
          <span style={{ marginLeft:'auto', color: live?'var(--accent)':'inherit' }}><VIcon name="clock" size={10} style={{verticalAlign:'-1px'}} /> {elapsed}</span>
        </div>
        <div className={`ocard-act ${actCls}`}>
          {s.status==='running' ? <Spark /> : actIcon ? <VIcon name={actIcon} size={13} /> : <VIcon name="clock" size={12} />}
          <span>{s.activity}</span>
        </div>
        {s.status==='running' ? (
          <div className="indet" />
        ) : ds.n>0 ? (
          <>
            <div className="dbar">
              <i className="a" style={{ width:`${ds.add/total*100}%` }} />
              <i className="d" style={{ width:`${ds.del/total*100}%` }} />
            </div>
            <div className="dbar-meta">
              <span className="fc-add">+{ds.add}</span>
              <span className="fc-del">−{ds.del}</span>
              <span style={{ color:'var(--txt-2)' }}>{ds.n} file{ds.n>1?'s':''}</span>
            </div>
          </>
        ) : <div style={{ height:6 }} />}
      </div>
      <div className="ocard-foot">
        <span className="tk"><VIcon name="bolt" size={11} /> {tokens} tok</span>
        <span className="sp" />
        {ds.n>0 && <button className="mini-btn" onClick={e=>{ e.stopPropagation(); onReview(s.id); }}><VIcon name="layers" size={13} /> Diff</button>}
        <button className="mini-btn accent" onClick={e=>{ e.stopPropagation(); onOpen(s.id); }}><VIcon name="chevright" size={13} /> Open</button>
      </div>
    </div>
  );
}

function KPI({ cls, icon, label, value, unit, sub }){
  return (
    <div className={`kpi ${cls}`}>
      <div className="k-lbl"><VIcon name={icon} size={12} /> {label}</div>
      <div className="k-val">{value}{unit && <small>{unit}</small>}</div>
      <div className="k-sub">{sub}</div>
    </div>
  );
}

function OrchestratorView({ sessions, tokens, onOpen, onReview, onNew }){
  useTick(true); // keep aggregate fleet timer alive
  const by = st => sessions.filter(s=>s.status===st);
  const running = by('running'), waiting = by('waiting'), done = by('done'), error = by('error');
  const live = sessions.filter(s=>s.status==='running'||s.status==='waiting');
  const recent = sessions.filter(s=>s.status==='done'||s.status==='error'||s.status==='idle');
  const totalTok = sessions.reduce((a,s)=>a+(tokens[s.id]||0),0);
  const tps = running.length * 14; // synthetic aggregate throughput

  return (
    <div className="main">
      <div className="view-head">
        <VIcon name="grid" size={16} style={{ color:'var(--accent)' }} />
        <span className="ttl">Orchestrator</span>
        <span className="lbl">{VLIST.length} runtimes · {VPROJECT}</span>
        <span style={{ flex:1 }} />
        <button className="tabbtn accent" onClick={onNew}><VIcon name="plus" size={14} /> New Agent</button>
      </div>
      <div className="view-scroll">
        <div className="orch-page">
          <div className="kpi-row">
            <KPI cls="run"  icon="bolt"  label="Active"    value={running.length} sub={running.length?'agents executing':'idle'} />
            <KPI cls="wait" icon="warn"  label="Awaiting"  value={waiting.length} sub="need your input" />
            <KPI cls="done" icon="check" label="Completed" value={done.length} sub="this session" />
            <KPI cls="err"  icon="alert" label="Failed"    value={error.length} sub="need attention" />
            <KPI cls="fleet" icon="layers" label="Fleet throughput" value={(totalTok/1000).toFixed(1)} unit="k tok" sub={`~${tps} tok/s live`} />
          </div>

          {live.length>0 && (
            <div className="orch-sec">
              <div className="orch-sec-h">
                <span className="lbl">Live workers</span>
                <span className="rule" />
                <span className="lbl" style={{ color:'var(--accent)' }}>{live.length} running</span>
              </div>
              <div className="orch-grid">
                {live.map(s=>(
                  <OrchCard key={s.id} s={s} tokens={fmtTok(tokens[s.id]||0)} onOpen={onOpen} onReview={onReview} />
                ))}
              </div>
            </div>
          )}

          {recent.length>0 && (
            <div className="orch-sec">
              <div className="orch-sec-h">
                <span className="lbl">Recent &amp; completed</span>
                <span className="rule" />
                <span className="lbl">{recent.length}</span>
              </div>
              <div className="orch-grid">
                {recent.map(s=>(
                  <OrchCard key={s.id} s={s} tokens={fmtTok(tokens[s.id]||0)} onOpen={onOpen} onReview={onReview} />
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
function fmtTok(n){ return n>=1000 ? (n/1000).toFixed(1)+'k' : String(n); }

/* ============================================================
   ACTIVITY HISTORY
   ============================================================ */
const EV_ICON = { spawn:'bolt', read:'eye', edit:'edit', cmd:'terminal', done:'check', error:'alert', wait:'warn', merge:'branch', review:'layers' };

function buildHistory(sessions){
  const ev = [];
  const push = (t, agentId, session, sid, status, ico, html) => ev.push({ t, agentId, session, sid, status, ico, html });

  sessions.forEach(s=>{
    let cursor = s.startedAt;
    push(cursor, s.agentId, s.title, s.id, 'info', 'spawn', { lead:'Spawned worker on', code:s.branch });
    s.blocks.forEach(b=>{
      cursor += 24000 + Math.floor(Math.random()*40000);
      if (b.t==='tool'){
        const map = { read:['read','Read', 'info'], edit:['edit','Edited','ok'], cmd:['cmd','Ran','ok'] };
        const m = map[b.kind] || map.cmd;
        const failed = b.stat && /exit 1|error/i.test(b.stat);
        push(cursor, s.agentId, s.title, s.id, failed?'err':m[2], m[0], { lead:m[1], code:b.name });
      }
    });
    cursor += 30000;
    if (s.status==='done')    push(cursor, s.agentId, s.title, s.id, 'ok',   'done',  { lead:'Completed —', plain:s.activity.replace('completed · ','') });
    if (s.status==='error')   push(cursor, s.agentId, s.title, s.id, 'err',  'error', { lead:'Failed —', plain:s.activity.replace('failed · ','') });
    if (s.status==='waiting') push(cursor, s.agentId, s.title, s.id, 'wait', 'wait',  { lead:'Awaiting approval —', plain:'destructive migration' });
    if (s.status==='running') push(cursor, s.agentId, s.title, s.id, 'run',  'cmd',   { lead:'Working —', plain:s.activity });
  });

  // a few prior-day events for texture
  const yest = Date.now() - 86400000;
  push(yest+ 3600000, 'cc', 'Add OpenTelemetry tracing', 'h-1', 'ok',  'merge', { lead:'Merged', code:'agent/otel-tracing', plain:'into main' });
  push(yest+ 3300000, 'gx', 'Add OpenTelemetry tracing', 'h-2', 'ok',  'done',  { lead:'Completed —', plain:'spans wired across 4 services' });
  push(yest+ 2400000, 'ag', 'Backfill search index',     'h-3', 'err', 'error', { lead:'Failed —', plain:'OOM at 1.2M docs, retried with batching' });
  push(yest+ 1800000, 'cc', 'Tighten CSP headers',        'h-4', 'ok',  'merge', { lead:'Merged', code:'agent/csp-hardening', plain:'into main' });

  return ev.sort((a,b)=>b.t-a.t);
}

function HistoryRow({ e, onOpen }){
  const a = VAGENTS[e.agentId];
  const c = e.html;
  return (
    <div className="hrow" onClick={()=>onOpen && onOpen(e.sid)}>
      <span className="time">{timeStr(e.t)}</span>
      <span className="badge-mini" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</span>
      <span className={`ev-ico ${e.status}`}><VIcon name={EV_ICON[e.ico]||'dots'} size={13} /></span>
      <span className="txt">
        <b>{e.session}</b> · {c.lead} {c.code && <code>{c.code}</code>} {c.plain && <span>{c.plain}</span>}
      </span>
      <span className={`tag ${e.status}`}>{a.cli}</span>
    </div>
  );
}

function HistoryView({ sessions, onOpen }){
  const all = React.useMemo(()=>buildHistory(sessions), [sessions]);
  const [q, setQ] = React.useState('');
  const [agent, setAgent] = React.useState('all');
  const [status, setStatus] = React.useState('all');

  const STAT_FILTERS = [['all','All'],['run','Active'],['ok','Done'],['wait','Approvals'],['err','Failed']];

  const filtered = all.filter(e=>{
    if (agent!=='all' && e.agentId!==agent) return false;
    if (status!=='all' && e.status!==status && !(status==='ok'&&e.status==='info')) return false;
    if (q){
      const hay = (e.session+' '+(e.html.code||'')+' '+(e.html.plain||'')+' '+e.html.lead).toLowerCase();
      if (!hay.includes(q.toLowerCase())) return false;
    }
    return true;
  });

  const today = [], yesterday = [];
  const dayStart = new Date(); dayStart.setHours(0,0,0,0);
  filtered.forEach(e=> (e.t>=dayStart.getTime()?today:yesterday).push(e));

  const Group = ({ label, items }) => items.length===0 ? null : (
    <>
      <div className="hist-day">
        <span className="d">{label}</span>
        <span className="rule" />
        <span className="ct">{items.length} events</span>
      </div>
      <div className="hist-feed">
        {items.map((e,i)=><HistoryRow key={i} e={e} onOpen={onOpen} />)}
      </div>
    </>
  );

  return (
    <div className="main">
      <div className="view-head">
        <VIcon name="history" size={16} style={{ color:'var(--accent)' }} />
        <span className="ttl">Activity History</span>
        <span className="lbl">{all.length} events · all runtimes</span>
      </div>
      <div className="view-scroll">
        <div className="hist-page">
          <div className="hist-bar">
            <div className="hist-search">
              <VIcon name="search" size={14} />
              <input placeholder="Search activity…" value={q} onChange={e=>setQ(e.target.value)} />
            </div>
            <div className="chip-group">
              <div className={`chip ${agent==='all'?'on':''}`} onClick={()=>setAgent('all')}>All agents</div>
              {VLIST.map(a=>(
                <div key={a.id} className={`chip ${agent===a.id?'on':''}`} onClick={()=>setAgent(a.id)}>
                  <span className="badge-mini" style={{ color:a.tint }}>{a.badge}</span>{a.name.split(' ')[0]}
                </div>
              ))}
            </div>
            <span style={{ flex:1 }} />
            <div className="chip-group">
              {STAT_FILTERS.map(([v,l])=>(
                <div key={v} className={`chip ${status===v?'on':''}`} onClick={()=>setStatus(v)}>{l}</div>
              ))}
            </div>
          </div>

          {filtered.length===0
            ? <div className="hist-empty">No activity matches your filters.</div>
            : <><Group label="Today" items={today} /><Group label="Yesterday" items={yesterday} /></>}
        </div>
      </div>
    </div>
  );
}

/* ============================================================
   SCHEDULED TASKS
   ============================================================ */
const SCHEDULED = [
  { agentId:'cc', title:'Nightly dependency audit', cadence:'Every day · 02:00', target:'main', next:'in 6h 12m' },
  { agentId:'gx', title:'Regenerate API client from OpenAPI spec', cadence:'On push to spec/', target:'agent/api-client', next:'on trigger' },
  { agentId:'ag', title:'Weekly changelog draft', cadence:'Mondays · 09:00', target:'agent/changelog', next:'in 3d 4h' },
];
function ScheduledView({ onNew }){
  return (
    <div className="main">
      <div className="view-head">
        <VIcon name="calendar" size={16} style={{ color:'var(--accent)' }} />
        <span className="ttl">Scheduled Tasks</span>
        <span className="lbl">{SCHEDULED.length} recurring jobs</span>
        <span style={{ flex:1 }} />
        <button className="tabbtn accent" onClick={onNew}><VIcon name="plus" size={14} /> New schedule</button>
      </div>
      <div className="view-scroll">
        <div className="sched-page">
          {SCHEDULED.map((j,i)=>{
            const a = VAGENTS[j.agentId];
            return (
              <div className="sched-card" key={i}>
                <div className="sched-ico"><VIcon name="clock" size={18} /></div>
                <div className="sched-main">
                  <div className="sched-t">{j.title}</div>
                  <div className="sched-d">
                    <span>{a.badge} · {a.name}</span>
                    <span><VIcon name="refresh" size={10} style={{verticalAlign:'-1px'}} /> {j.cadence}</span>
                    <span><VIcon name="branch" size={10} style={{verticalAlign:'-1px'}} /> {j.target}</span>
                  </div>
                </div>
                <div className="sched-next"><small>Next run</small>{j.next}</div>
                <button className="mini-btn"><VIcon name="dots" size={14} /></button>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { OrchestratorView, HistoryView, ScheduledView });
