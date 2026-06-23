/* ============================================================
   am-app.jsx — root: state, streaming engine, modal, review drawer
   (rev) ============================================================ */
const { Icon: AIcon, AGENTS: AAGENTS, AGENT_LIST: AALIST, SESSIONS: ASESSIONS } = window.AM;
const { useState, useEffect, useRef } = React;

function fmtTokens(n){ return n>=1000 ? (n/1000).toFixed(1)+'k' : String(n); }
function clone(x){ return JSON.parse(JSON.stringify(x)); }

/* ---------- Titlebar menu bar (functional dropdowns) ---------- */
function MenuBar({ menus }){
  const [open, setOpen] = useState(null);
  useEffect(()=>{
    if (open===null) return;
    const h = ()=>setOpen(null);
    setTimeout(()=>window.addEventListener('click', h), 0);
    return ()=>window.removeEventListener('click', h);
  },[open]);
  return (
    <div className="tb-menu">
      {menus.map(m=>(
        <div className="tb-menu-wrap" key={m.label}>
          <button className={open===m.label?'open':''}
            onClick={e=>{ e.stopPropagation(); setOpen(o=>o===m.label?null:m.label); }}
            onMouseEnter={()=>{ if(open!==null) setOpen(m.label); }}>
            {m.label}
          </button>
          {open===m.label && (
            <div className="menu tb-dropdown" onClick={e=>e.stopPropagation()}>
              {m.items.map((it,i)=> it.sep
                ? <div className="menu-sep" key={i} />
                : (
                  <div className={`menu-item ${it.active?'sel':''}`} key={i}
                    onClick={()=>{ it.onClick && it.onClick(); setOpen(null); }}>
                    <AIcon name={it.active?'check':(it.icon||'dots')} size={14} />
                    <span>{it.label}</span>
                    {it.kbd && <span className="kbd">{it.kbd}</span>}
                  </div>
                ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

/* ---------- About / shortcuts modal ---------- */
function AboutModal({ onClose }){
  const SHORTCUTS = [
    ['⌘N','Spawn a new agent'],
    ['⌘1 / ⌘2 / ⌘3','Orchestrator · History · Scheduled'],
    ['⌘D','Toggle diff review panel'],
    ['⌘,','Open settings'],
    ['Enter','Send message to active agent'],
  ];
  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" style={{ width:460 }} onClick={e=>e.stopPropagation()}>
        <div className="modal-h">
          <span className="lbl">About</span>
          <span style={{ color:'var(--txt-0)', fontWeight:600, fontSize:13 }}>AgentManager</span>
          <button className="x" onClick={onClose}><AIcon name="x" size={16} /></button>
        </div>
        <div className="modal-b">
          <div style={{ display:'flex', alignItems:'center', gap:13, marginBottom:14 }}>
            <div className="tb-logo" style={{ width:40, height:40, border:'1px solid var(--line-bright)', borderRadius:'var(--r-md)', display:'grid', placeItems:'center', background:'var(--bg-1)' }}>
              <AIcon name="layers" size={22} style={{ color:'var(--accent)' }} />
            </div>
            <div>
              <div style={{ fontFamily:'var(--mono)', fontSize:13, color:'var(--txt-0)' }}>Agent<b style={{ color:'var(--accent)' }}>Manager</b> 1.4.0</div>
              <div className="lbl" style={{ fontSize:9 }}>build 20260613 · 3 runtimes registered</div>
            </div>
          </div>
          <p style={{ fontSize:12.5, color:'var(--txt-1)', lineHeight:1.6, marginBottom:16 }}>
            Run Claude Code, GPT&nbsp;/&nbsp;Codex, and the Antigravity CLI side by side — each in its own isolated git worktree, orchestrated from a single client.
          </p>
          <div className="lbl" style={{ marginBottom:10 }}>Keyboard shortcuts</div>
          <div style={{ display:'flex', flexDirection:'column', gap:0 }}>
            {SHORTCUTS.map(([k,d],i)=>(
              <div key={i} style={{ display:'flex', alignItems:'center', justifyContent:'space-between', padding:'8px 0', borderTop: i?'1px solid var(--line-soft)':'none' }}>
                <span style={{ fontSize:12.5, color:'var(--txt-1)' }}>{d}</span>
                <span className="kbd" style={{ fontFamily:'var(--mono)', fontSize:11, color:'var(--accent)', background:'var(--bg-1)', border:'1px solid var(--line)', padding:'3px 9px', borderRadius:'var(--r-sm)' }}>{k}</span>
              </div>
            ))}
          </div>
        </div>
        <div className="modal-f">
          <button className="btn-primary" onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}

/* ---------- Review drawer ---------- */
function ReviewDrawer({ open, session, onClose }){
  const [fileIdx, setFileIdx] = useState(0);
  useEffect(()=>{ setFileIdx(0); },[session && session.id]);
  const files = (session && session.files) || [];
  const file = files[fileIdx];
  const totAdd = files.reduce((s,f)=>s+f.add,0);
  const totDel = files.reduce((s,f)=>s+f.del,0);
  return (
    <div className={`review ${open?'open':''}`}>
      <div className="rv-head">
        <AIcon name="layers" size={16} />
        <span className="t">Diff Review</span>
        <span className="code-chip">+{totAdd}/−{totDel}</span>
        <span style={{ flex:1 }} />
        <button className="icobtn" style={{ width:28, height:28 }} onClick={onClose}><AIcon name="x" size={15} /></button>
      </div>
      <div className="rv-files">
        {files.map((f,i)=>(
          <div key={i} className={`rv-file ${i===fileIdx?'active':''}`} onClick={()=>setFileIdx(i)}>
            <AIcon name="file" size={14} />
            <span className="nm">{f.name}</span>
            <span className="ch"><span className="fc-add">+{f.add}</span> <span className="fc-del">−{f.del}</span></span>
          </div>
        ))}
        {files.length===0 && <div className="rv-file" style={{ cursor:'default' }}>No changes yet</div>}
      </div>
      <div className="rv-diff">
        {file && file.hunks.map((h,hi)=>(
          <div key={hi}>
            <div className="dl hunk"><span className="gut" /><span className="c">{h.h}</span></div>
            {h.lines.map((ln,li)=>{
              const [k,txt] = ln;
              const cls = k==='add'?'add':k==='del'?'del':'';
              const mark = k==='add'?'+':k==='del'?'−':'';
              return <div key={li} className={`dl ${cls}`}><span className="gut">{mark}</span><span className="c">{txt||' '}</span></div>;
            })}
          </div>
        ))}
      </div>
      <div className="rv-foot">
        <button className="rv-keep" onClick={onClose}><AIcon name="check" size={13} style={{verticalAlign:'-2px'}} /> Keep changes</button>
        <button className="rv-discard" onClick={onClose}>Discard</button>
      </div>
    </div>
  );
}

/* ---------- New-agent modal ---------- */
const TASK_HINTS = {
  cc:'Refactor the rate limiter to a sliding window',
  gx:'Add property-based tests for the parser',
  ag:'Generate the OpenAPI spec from the route handlers',
};
function NewAgentModal({ onClose, onCreate }){
  const [agentId, setAgentId] = useState('cc');
  const [title, setTitle] = useState('');
  const agent = AAGENTS[agentId];
  const [model, setModel] = useState(agent.model);
  const [mMenu, setMMenu] = useState(false);
  useEffect(()=>{ setModel(AAGENTS[agentId].model); },[agentId]);
  const create = ()=> onCreate({ agentId, title: title.trim() || TASK_HINTS[agentId], model });
  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" onClick={e=>e.stopPropagation()}>
        <div className="modal-h">
          <span className="lbl">New worker</span>
          <span style={{ color:'var(--txt-0)', fontWeight:600, fontSize:13 }}>Spawn an agent</span>
          <button className="x" onClick={onClose}><AIcon name="x" size={16} /></button>
        </div>
        <div className="modal-b">
          <div className="field">
            <span className="lbl">Runtime</span>
            <div className="agent-grid">
              {AALIST.map(a=>(
                <div key={a.id} className={`agent-opt ${agentId===a.id?'sel':''}`} onClick={()=>setAgentId(a.id)}>
                  <div className="badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
                  <div className="nm">{a.name}</div>
                  <div className="ds">{a.desc}</div>
                </div>
              ))}
            </div>
          </div>
          <div className="modal-row">
            <div className="field" style={{ flex:2 }}>
              <span className="lbl">Task</span>
              <input className="modal-input" placeholder={TASK_HINTS[agentId]} value={title}
                onChange={e=>setTitle(e.target.value)} autoFocus
                onKeyDown={e=>{ if(e.key==='Enter') create(); }} />
            </div>
            <div className="field" style={{ flex:1, position:'relative' }}>
              <span className="lbl">Model</span>
              <div className="select-pill" onClick={()=>setMMenu(m=>!m)}>
                <AIcon name="spark" size={13} /><span style={{ flex:1 }}>{model}</span><AIcon name="chevdown" size={13} />
              </div>
              {mMenu && (
                <div className="menu" style={{ top:'62px', left:0, right:0 }}>
                  {agent.models.map(m=>(
                    <div key={m} className={`menu-item ${m===model?'sel':''}`} onClick={()=>{ setModel(m); setMMenu(false); }}>
                      <AIcon name={m===model?'check':'spark'} size={13} /><span>{m}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
          <div className="field" style={{ marginBottom:0 }}>
            <span className="lbl">Worktree</span>
            <div className="select-pill" style={{ cursor:'default' }}>
              <AIcon name="branch" size={13} /><span style={{ flex:1 }}>agent/{(title||TASK_HINTS[agentId]).toLowerCase().replace(/[^a-z0-9]+/g,'-').slice(0,28).replace(/-$/,'')}</span>
              <span className="lbl" style={{ fontSize:9 }}>isolated</span>
            </div>
          </div>
        </div>
        <div className="modal-f">
          <button className="btn-ghost" onClick={onClose}>Cancel</button>
          <button className="btn-primary" onClick={create}><AIcon name="bolt" size={14} /> Launch agent</button>
        </div>
      </div>
    </div>
  );
}

/* ---------- generated stream text for new / replied sessions ---------- */
function replyStream(agentId, task){
  const a = AAGENTS[agentId];
  return `Spinning up an isolated worktree and reading the repository to scope "${task}". Indexing the relevant modules now, drafting a plan, and I'll start applying changes incrementally — running ${a.cli==='codex'?'the test suite':'a typecheck'} after each edit so nothing regresses before I hand it back for review…`;
}

/* ---------- seed delegations (sample: 2 ready reports on session s1) ---------- */
const SEED_DELEGATIONS = {
  d1: { id:'d1', sessionId:'s1', agentId:'cc', workerName:'Atlas', model:'Claude Sonnet 4.5',
        tr:{ on:true, from:'KO', to:'EN' }, status:'ready',
        prompt:'billing.ts / projects.ts 의 인라인 토큰 검증을 제거하고 requireAuth 미들웨어 적용이 회귀 없이 동작하는지 검증하는 테스트를 작성해줘.',
        report:'requireAuth 적용 후 billing·projects·workspaces 라우트의 401 경로를 점검했고, 회귀 테스트 12건을 추가해 전부 통과했습니다. refresh 토큰 로테이션도 그대로 유지됩니다. 누락된 admin.ts 경로 1건을 발견해 함께 수정했습니다.' },
  d2: { id:'d2', sessionId:'s1', agentId:'ag', workerName:'Vega', model:'Gemini 3.5 Flash',
        tr:{ on:true, from:'KO', to:'EN' }, status:'ready',
        prompt:'AuthError 유니온 타입에 대한 문서와 사용 예시를 docs/auth.md 에 정리해줘.',
        report:'docs/auth.md 에 AuthError 유니온(missing·expired·malformed)의 의미와 호출부 처리 예시를 정리했습니다. 401 응답 스키마 표와 마이그레이션 노트를 포함했습니다.' },
};

/* ---------- App ---------- */
function App(){
  const [sessions, setSessions] = useState(()=>{
    const ss = clone(ASESSIONS);
    const s1 = ss.find(s=>s.id==='s1');
    if (s1) s1.blocks = [...s1.blocks, { t:'delegate', did:'d1' }, { t:'delegate', did:'d2' }];
    return ss;
  });
  const [workers, setWorkers] = useState(()=>clone(window.AM.WORKERS));
  const [delegations, setDelegations] = useState(()=>clone(SEED_DELEGATIONS));
  const [assign, setAssign] = useState(null);   // { sourceText, startNew }
  const [noIdle, setNoIdle] = useState(null);
  const [activeId, setActiveId] = useState('s1');
  const [nav, setNav] = useState('orchestrator');
  const [drafts, setDrafts] = useState({});
  const [shown, setShown] = useState(()=>{
    const o = {}; clone(ASESSIONS).forEach(s=>{ o[s.id] = 0; }); return o;
  });
  const [tokens, setTokens] = useState(()=>{
    const o = {}; ASESSIONS.forEach(s=>{ o[s.id] = 2400 + Math.floor(Math.random()*9000); }); return o;
  });
  const [reviewOpen, setReviewOpen] = useState(false);
  const [modal, setModal] = useState(false);
  const [about, setAbout] = useState(false);

  const sessionsRef = useRef(sessions); sessionsRef.current = sessions;

  /* streaming engine — reveals stream blocks + ticks tokens for all running sessions */
  useEffect(()=>{
    const id = setInterval(()=>{
      setShown(prev=>{
        const next = { ...prev };
        sessionsRef.current.forEach(s=>{
          if (s.status!=='running') return;
          const last = s.blocks[s.blocks.length-1];
          if (last && last.t==='stream'){
            const full = last.full.length;
            if ((next[s.id]||0) < full) next[s.id] = Math.min(full, (next[s.id]||0) + 3);
          }
        });
        return next;
      });
      setTokens(prev=>{
        const next = { ...prev };
        sessionsRef.current.forEach(s=>{ if (s.status==='running') next[s.id] = (next[s.id]||0) + Math.floor(8+Math.random()*22); });
        return next;
      });
    }, 45);
    return ()=>clearInterval(id);
  },[]);

  const active = sessions.find(s=>s.id===activeId);

  const setDraft = v => setDrafts(d=>({ ...d, [activeId]: v }));
  const setModel = m => setSessions(ss=>ss.map(s=>s.id===activeId?{...s, model:m}:s));

  /* freeze an active stream block into static prose */
  function freezeStream(s){
    const blocks = s.blocks.slice();
    const last = blocks[blocks.length-1];
    if (last && last.t==='stream'){
      blocks[blocks.length-1] = { t:'agent', prose:(last.prose_pre||[]).concat([last.full]) };
    }
    return { ...s, blocks };
  }

  const sendMessage = ()=>{
    const text = (drafts[activeId]||'').trim();
    if (!text) return;
    setSessions(ss=>ss.map(s=>{
      if (s.id!==activeId) return s;
      let ns = freezeStream(s);
      const full = `Got it — picking that up now. ${replyStream(s.agentId, text)}`;
      ns = { ...ns, status:'running', activity:'processing your follow-up',
        blocks:[...ns.blocks, { t:'user', text }, { t:'stream', prose_pre:[], full }] };
      return ns;
    }));
    setShown(p=>({ ...p, [activeId]:0 }));
    setDrafts(d=>({ ...d, [activeId]:'' }));
  };

  const spawnAgent = ({ agentId, title, model })=>{
    const id = 'n'+Date.now();
    const branch = 'agent/'+title.toLowerCase().replace(/[^a-z0-9]+/g,'-').slice(0,28).replace(/-$/,'');
    const full = replyStream(agentId, title);
    const ns = {
      id, agentId, title, branch, status:'running', model,
      startedAt: Date.now(), project: window.AM.PROJECT,
      activity:'initializing worktree',
      blocks:[ { t:'user', text:title }, { t:'stream', prose_pre:[], full } ],
      files:[],
    };
    setSessions(ss=>[ns, ...ss]);
    setShown(p=>({ ...p, [id]:0 }));
    setTokens(p=>({ ...p, [id]:Math.floor(400+Math.random()*900) }));
    setActiveId(id);
    setNav('session');
    setModal(false);
  };

  const approve = (sid)=>{
    setSessions(ss=>ss.map(s=>{
      if (s.id!==sid) return s;
      const blocks = s.blocks.filter(b=>b.t!=='approve');
      const full = "Approved. Applying migration 0042 against staging — creating the partitioned table, backfilling 2.1M rows in 50k batches (now at batch 9/42), then swapping table names inside a single transaction. I'll verify row counts match before dropping the legacy table…";
      blocks.push({ t:'agent', prose:["**Approved.** Running the migration against staging."] });
      blocks.push({ t:'stream', prose_pre:[], full });
      return { ...s, status:'running', activity:'backfilling usage_events (batch 9/42)', blocks };
    }));
    setShown(p=>({ ...p, [sid]:0 }));
  };
  const reject = (sid)=>{
    setSessions(ss=>ss.map(s=>{
      if (s.id!==sid) return s;
      const blocks = s.blocks.filter(b=>b.t!=='approve');
      blocks.push({ t:'agent', prose:["Understood — holding off. The migration file is staged at `migrations/0042_usage_events_v3.sql` for manual review whenever you're ready."] });
      return { ...s, status:'idle', activity:'paused by reviewer', blocks };
    }));
  };

  const openReview = (s)=>{ const sid = typeof s==='string'?s:s.id; setActiveId(sid); setNav('session'); setReviewOpen(true); };
  const openSession = (id)=>{ setActiveId(id); setNav('session'); };
  const quickSpawn = (agentId)=> spawnAgent({ agentId, title: TASK_HINTS[agentId], model: AAGENTS[agentId].model });

  /* ---------- worker delegation ---------- */
  const prefillFrom = (session)=>{
    for (let i=session.blocks.length-1;i>=0;i--){
      const b = session.blocks[i];
      if (b.t==='agent' && b.prose){ const s = b.prose.find(x=>typeof x==='string'); if (s) return s.slice(0,180); }
    }
    return session.title;
  };
  const handleDelegate = (session)=>{
    const src = prefillFrom(session);
    const idle = workers.filter(w=>w.status==='idle');
    if (idle.length===0) setNoIdle({ sourceText:src });
    else setAssign({ sourceText:src, startNew:false });
  };
  const confirmDelegate = ({ worker, prompt, worktree, autoInject })=>{
    const did = 'dg'+Date.now();
    const sid = activeId;
    // mark worker busy (add if new)
    setWorkers(ws=> worker.isNew ? [...ws, { id:worker.id, agentId:worker.agentId, name:worker.name, model:worker.model, tr:worker.tr, status:'busy' }]
                                 : ws.map(w=>w.id===worker.id?{...w, status:'busy'}:w));
    setDelegations(d=>({ ...d, [did]:{ id:did, sessionId:sid, agentId:worker.agentId, workerName:worker.name, model:worker.model, tr:worker.tr, status:'running', prompt, autoInject } }));
    setSessions(ss=>ss.map(s=>s.id===sid?{...s, blocks:[...s.blocks, { t:'delegate', did }]}:s));
    setAssign(null);
    // simulate the worker finishing
    setTimeout(()=>{
      const report = `위임 작업을 완료했습니다. 변경을 적용하고 검증까지 마쳤으며, 회귀 없이 통과했습니다. (“${prompt.slice(0,46)}${prompt.length>46?'…':''}”)`;
      setDelegations(d=> d[did] ? { ...d, [did]:{ ...d[did], status:'ready', report } } : d);
      setWorkers(ws=>ws.map(w=>w.id===worker.id?{...w, status:'idle'}:w));
      if (autoInject) setDrafts(dr=>({ ...dr, [sid]: (dr[sid]?dr[sid]+'\n\n':'') + '[워커 보고] ' + report }));
    }, 4200);
  };
  const pasteReport = (did)=>{
    const del = delegations[did]; if(!del) return;
    setDrafts(dr=>({ ...dr, [activeId]: (dr[activeId]?dr[activeId]+'\n\n':'') + '[워커 보고 · '+del.workerName+'] ' + del.report }));
    setDelegations(d=>({ ...d, [did]:{ ...d[did], pasted:true } }));
  };
  const mergePaste = ()=>{
    const ready = Object.values(delegations).filter(d=>d.status==='ready' && !d.pasted);
    if (ready.length===0) return;
    const merged = ready.map(d=>'[워커 보고 · '+d.workerName+'] '+d.report).join('\n\n');
    setDrafts(dr=>({ ...dr, [activeId]: (dr[activeId]?dr[activeId]+'\n\n':'') + merged }));
    setDelegations(d=>{ const n={...d}; ready.forEach(r=>{ n[r.id]={...n[r.id], pasted:true}; }); return n; });
  };
  const redelegate = (del)=> setAssign({ sourceText:del.prompt, startNew:false });
  const openWorkerSession = ()=>{ /* would focus the worker's own session */ };
  const readyReports = Object.values(delegations).filter(d=>d.status==='ready' && !d.pasted);

  /* keyboard shortcuts (mirror the menu bar) */
  useEffect(()=>{
    const h = (e)=>{
      if (e.metaKey || e.ctrlKey){
        const k = e.key.toLowerCase();
        if (k==='n'){ e.preventDefault(); setModal(true); }
        else if (k==='1'){ e.preventDefault(); setNav('orchestrator'); }
        else if (k==='2'){ e.preventDefault(); setNav('history'); }
        else if (k==='3'){ e.preventDefault(); setNav('scheduled'); }
        else if (k===','){ e.preventDefault(); setNav('settings'); }
        else if (k==='d'){ e.preventDefault(); setReviewOpen(o=>!o); }
      } else if (e.key==='Escape'){ setModal(false); setAbout(false); }
    };
    window.addEventListener('keydown', h);
    return ()=>window.removeEventListener('keydown', h);
  },[]);

  const menus = [
    { label:'Agents', items:[
      { label:'New agent…', icon:'plus', kbd:'⌘N', onClick:()=>setModal(true) },
      { sep:true },
      { label:'Run Claude Code', icon:'bolt', onClick:()=>quickSpawn('cc') },
      { label:'Run GPT / Codex', icon:'bolt', onClick:()=>quickSpawn('gx') },
      { label:'Run Antigravity CLI', icon:'bolt', onClick:()=>quickSpawn('ag') },
      { sep:true },
      { label:'Orchestrator dashboard', icon:'grid', active:nav==='orchestrator', onClick:()=>setNav('orchestrator') },
    ]},
    { label:'View', items:[
      { label:'Orchestrator', icon:'grid', kbd:'⌘1', active:nav==='orchestrator', onClick:()=>setNav('orchestrator') },
      { label:'Activity History', icon:'history', kbd:'⌘2', active:nav==='history', onClick:()=>setNav('history') },
      { label:'Scheduled Tasks', icon:'calendar', kbd:'⌘3', active:nav==='scheduled', onClick:()=>setNav('scheduled') },
      { sep:true },
      { label:'Diff review panel', icon:'panel', kbd:'⌘D', active:reviewOpen, onClick:()=>setReviewOpen(o=>!o) },
      { label:'Settings', icon:'settings', kbd:'⌘,', active:nav==='settings', onClick:()=>setNav('settings') },
    ]},
    { label:'Help', items:[
      { label:'Keyboard shortcuts', icon:'ide', onClick:()=>setAbout(true) },
      { label:'Documentation', icon:'file', onClick:()=>setAbout(true) },
      { sep:true },
      { label:'About AgentManager', icon:'spark', onClick:()=>setAbout(true) },
    ]},
  ];

  return (
    <div className="app">
      {/* titlebar */}
      <div className="titlebar">
        <div className="tb-brand">
          <div className="tb-logo"><AIcon name="layers" size={15} style={{ color:'var(--accent)' }} /></div>
          <div className="tb-name">Agent<b>Manager</b></div>
        </div>
        <MenuBar menus={menus} />
        <div className="tb-spacer" />
        <div className="tb-stat">
          <div className="s"><div className="dot running" style={{ position:'static' }} /> {sessions.filter(s=>s.status==='running').length} RUNNING</div>
          <div className="s"><div className="dot waiting" /> {sessions.filter(s=>s.status==='waiting').length} WAITING</div>
          <div className="s"><div className="dot done" /> {sessions.filter(s=>s.status==='done').length} DONE</div>
        </div>
        <div className="tb-win">
          <button title="Minimize"><svg width="11" height="11" viewBox="0 0 11 11"><path d="M2 5.5h7" stroke="currentColor" strokeWidth="1"/></svg></button>
          <button title="Maximize"><svg width="11" height="11" viewBox="0 0 11 11"><rect x="2" y="2" width="7" height="7" fill="none" stroke="currentColor" strokeWidth="1"/></svg></button>
          <button className="close" title="Close"><svg width="11" height="11" viewBox="0 0 11 11"><path d="M2 2l7 7M9 2l-7 7" stroke="currentColor" strokeWidth="1"/></svg></button>
        </div>
      </div>

      <div className="body">
        <Sidebar
          sessions={sessions}
          activeId={activeId}
          onSelect={id=>{ setActiveId(id); setNav('session'); }}
          nav={nav} onNav={setNav}
          onNew={()=>setModal(true)}
        />

        {nav==='settings' ? (
          <SettingsView onClose={()=>setNav('orchestrator')} />
        ) : nav==='orchestrator' ? (
          <OrchestratorView sessions={sessions} tokens={tokens} onOpen={openSession} onReview={openReview} onNew={()=>setModal(true)} />
        ) : nav==='history' ? (
          <HistoryView sessions={sessions} onOpen={openSession} />
        ) : nav==='scheduled' ? (
          <ScheduledView onNew={()=>setModal(true)} />
        ) : active ? (
          <ChatPane
            key={active.id}
            session={active}
            draft={drafts[active.id]||''}
            onDraft={setDraft}
            onSend={sendMessage}
            onModel={setModel}
            streamShown={shown[active.id]||0}
            tokens={fmtTokens(tokens[active.id]||0)}
            onReview={openReview}
            reviewOpen={reviewOpen}
            onToggleReview={()=>setReviewOpen(o=>!o)}
            onApprove={approve}
            onReject={reject}
            delegations={delegations}
            onDelegate={handleDelegate}
            onPasteReport={pasteReport}
            onRedelegate={redelegate}
            onOpenWorker={openWorkerSession}
            readyReports={readyReports}
            onMergePaste={mergePaste}
          />
        ) : <div className="main"><div className="empty">No session selected</div></div>}

        <ReviewDrawer open={reviewOpen} session={active} onClose={()=>setReviewOpen(false)} />
      </div>

      {modal && <NewAgentModal onClose={()=>setModal(false)} onCreate={spawnAgent} />}
      {about && <AboutModal onClose={()=>setAbout(false)} />}
      {assign && (
        <window.WorkerAssignModal
          workers={workers}
          sourceText={assign.sourceText}
          startNew={assign.startNew}
          onClose={()=>setAssign(null)}
          onDelegate={confirmDelegate}
        />
      )}
      {noIdle && (
        <window.NoIdleWorkerModal
          workers={workers}
          onClose={()=>setNoIdle(null)}
          onCreate={()=>{ const src=noIdle.sourceText; setNoIdle(null); setAssign({ sourceText:src, startNew:true }); }}
        />
      )}
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
