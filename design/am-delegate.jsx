/* ============================================================
   am-delegate.jsx — Worker delegation UI
   · WorkerAssignModal  — delegate to / spawn a worker
   · NoIdleWorkerModal  — shown when no idle worker exists
   · DelegationCard     — inline transcript card
   · ReportInbox        — ready-report bar near the composer
   Reuses the existing design system (.overlay/.modal/.field/.select-pill/
   .menu/.agent-grid/.btn-ghost/.btn-primary, AGENTS[id].tint/line/soft).
   ============================================================ */
const DG = window.AM;
const DIcon = DG.Icon;
const DAGENTS = DG.AGENTS;
const DLIST = DG.AGENT_LIST;
const DLANGS = DG.LANGS;

/* small pill-dropdown (local to this module) */
function PillSelect({ value, options, icon='spark', onChange, width }){
  const [open, setOpen] = React.useState(false);
  React.useEffect(()=>{
    if(!open) return;
    const h = ()=>setOpen(false);
    setTimeout(()=>window.addEventListener('click', h), 0);
    return ()=>window.removeEventListener('click', h);
  },[open]);
  return (
    <div className="select-pill" style={{ minWidth:width||140, position:'relative' }} onClick={e=>{ e.stopPropagation(); setOpen(o=>!o); }}>
      {icon && <DIcon name={icon} size={13} />}
      <span style={{ flex:1 }}>{value}</span>
      <DIcon name="chevdown" size={13} />
      {open && (
        <div className="menu" style={{ top:'40px', left:0, right:0, maxHeight:200, overflow:'auto' }} onClick={e=>e.stopPropagation()}>
          {options.map(o=>(
            <div key={o} className={`menu-item ${o===value?'sel':''}`} onClick={()=>{ onChange(o); setOpen(false); }}>
              <DIcon name={o===value?'check':(icon||'spark')} size={13} /><span>{o}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function trLabel(tr){ return tr && tr.on ? `TR ON · ${tr.from}→${tr.to}` : 'TR OFF'; }

/* ---------- create-worker form (embedded in assign modal) ---------- */
function NewWorkerForm({ draft, set }){
  const a = DAGENTS[draft.agentId];
  return (
    <div className="wk-form">
      <div className="modal-row">
        <div className="field" style={{ flex:1, marginBottom:0 }}>
          <span className="lbl">Runtime</span>
          <div className="agent-grid">
            {DLIST.map(ag=>(
              <div key={ag.id} className={`agent-opt ${draft.agentId===ag.id?'sel':''}`} onClick={()=>set({ agentId:ag.id, model:ag.model })}>
                <div className="badge" style={{ color:ag.tint, borderColor:ag.line, background:ag.soft }}>{ag.badge}</div>
                <div className="nm">{ag.name}</div>
                <div className="ds">{ag.desc}</div>
              </div>
            ))}
          </div>
        </div>
      </div>
      <div className="modal-row">
        <div className="field" style={{ flex:2, marginBottom:0 }}>
          <span className="lbl">이름 (Name)</span>
          <input className="modal-input" placeholder="예: Nova" value={draft.name} onChange={e=>set({ name:e.target.value })} />
        </div>
        <div className="field" style={{ flex:1, marginBottom:0 }}>
          <span className="lbl">Model</span>
          <PillSelect value={draft.model} options={a.models} onChange={m=>set({ model:m })} width={null} />
        </div>
      </div>
      {/* translation policy — fixed to the worker */}
      <div className="field" style={{ marginBottom:0 }}>
        <span className="lbl">번역 정책 (Translation policy)</span>
        <div className="tr-policy">
          <div className="tr-policy-head">
            <span className="t"><DIcon name="translate" size={14} style={{ color:'var(--info)' }} /> 번역 {draft.tr.on?'ON':'OFF'}</span>
            <button className={`tgl ${draft.tr.on?'on':''}`} onClick={()=>set({ tr:{ ...draft.tr, on:!draft.tr.on } })}><span className="knob" /></button>
          </div>
          {draft.tr.on && (
            <div className="tr-langs">
              <div>
                <div className="lbl" style={{ marginBottom:5 }}>번역 전</div>
                <PillSelect value={draft.tr.from} options={DLANGS} icon="translate" onChange={v=>set({ tr:{ ...draft.tr, from:v } })} width={110} />
              </div>
              <DIcon name="chevright" size={16} className="arrow" style={{ marginTop:18, stroke:'var(--txt-2)' }} />
              <div>
                <div className="lbl" style={{ marginBottom:5 }}>번역 후</div>
                <PillSelect value={draft.tr.to} options={DLANGS} icon="translate" onChange={v=>set({ tr:{ ...draft.tr, to:v } })} width={110} />
              </div>
            </div>
          )}
          <div className="tr-cap"><DIcon name="pin" size={11} /> 이 값은 워커에 고정됩니다 — 위임마다 바꿀 수 없습니다.</div>
        </div>
      </div>
    </div>
  );
}

/* ---------- A · WorkerAssignModal ---------- */
function WorkerAssignModal({ workers, sourceText, startNew, onClose, onDelegate }){
  const idle = workers.filter(w=>w.status==='idle');
  const [sel, setSel] = React.useState(()=> startNew ? '__new__' : (idle[0] ? idle[0].id : '__new__'));
  const [newOpen, setNewOpen] = React.useState(!!startNew);
  const [draft, setDraft] = React.useState({ agentId:'cc', model:DAGENTS.cc.model, name:'', tr:{ on:true, from:'KO', to:'EN' } });
  const setD = patch => setDraft(d=>({ ...d, ...patch }));
  const [prompt, setPrompt] = React.useState(sourceText||'');
  const [worktree, setWorktree] = React.useState('독립');
  const [autoInject, setAutoInject] = React.useState(true);

  const pickWorker = (id)=>{ setSel(id); setNewOpen(false); };
  const openNew = ()=>{ setSel('__new__'); setNewOpen(o=>!o); };

  const newValid = draft.name.trim().length>0;
  const canDelegate = prompt.trim().length>0 && (sel==='__new__' ? newValid : !!sel);

  const submit = ()=>{
    if(!canDelegate) return;
    const worker = sel==='__new__'
      ? { id:'w-'+Date.now(), agentId:draft.agentId, name:draft.name.trim(), model:draft.model, tr:draft.tr, isNew:true }
      : workers.find(w=>w.id===sel);
    onDelegate({ worker, prompt:prompt.trim(), worktree, autoInject });
  };

  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" style={{ width:600, maxHeight:'88vh', display:'flex', flexDirection:'column' }} onClick={e=>e.stopPropagation()}>
        <div className="modal-h">
          <span className="lbl">Delegate</span>
          <span style={{ color:'var(--txt-0)', fontWeight:600, fontSize:13 }}>워커에 위임 (Delegate to worker)</span>
          <button className="x" onClick={onClose}><DIcon name="x" size={16} /></button>
        </div>

        <div className="modal-b" style={{ overflow:'auto' }}>
          {/* worker list */}
          <div className="field">
            <span className="lbl">워커 (Worker)</span>
            <div className="wk-list">
              {workers.map(w=>{
                const a = DAGENTS[w.agentId];
                const busy = w.status==='busy';
                const selected = sel===w.id;
                return (
                  <div key={w.id} className={`wk-row ${selected?'sel':''} ${busy?'busy':''}`}
                    style={selected?{ borderColor:a.line }:{}}
                    onClick={()=>!busy && pickWorker(w.id)}>
                    <div className="wk-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
                    <div className="wk-meta">
                      <div className="wk-name-row">
                        <span className="wk-name">{w.name}</span>
                        <span className="wk-model">{a.name}</span>
                      </div>
                      <div className="wk-model">{w.model}</div>
                    </div>
                    <div className="wk-tags">
                      <span className={`tr-chip ${w.tr.on?'on':''}`}><DIcon name="translate" size={10} /> {trLabel(w.tr)}</span>
                      {busy
                        ? <span className="st-chip busy"><span className="spin" style={{ width:9, height:9, borderWidth:1.5 }} /> 사용 중</span>
                        : <span className="st-chip idle"><span className="dot done" style={{ position:'static' }} /> Idle</span>}
                      <span className="wk-check" style={selected?{ background:a.tint, color:'#1a0c06' }:{}}>
                        {selected && <DIcon name="check" size={12} />}
                      </span>
                    </div>
                  </div>
                );
              })}
              {/* + new worker */}
              <button className={`wk-add ${newOpen?'open':''}`} onClick={openNew}>
                <span className="ic"><DIcon name="plus" size={15} /></span>
                <span style={{ flex:1, textAlign:'left' }}>새 워커 (New worker)</span>
                <DIcon name={newOpen?'chevup':'chevdown'} size={14} />
              </button>
            </div>
            {newOpen && <NewWorkerForm draft={draft} set={setD} />}
          </div>

          {/* delegation prompt */}
          <div className="field">
            <span className="lbl">위임 프롬프트 (Delegation prompt)</span>
            <textarea className="modal-area" value={prompt} onChange={e=>setPrompt(e.target.value)}
              placeholder="이 워커에게 맡길 작업을 적어주세요…" rows={4} />
          </div>

          {/* worktree */}
          <div className="field" style={{ marginBottom:0 }}>
            <span className="lbl">Worktree</span>
            <PillSelect value={worktree} options={['독립','공유']} icon="branch" onChange={setWorktree} width={150} />
          </div>
        </div>

        <div className="modal-f">
          <label className={`chk ${autoInject?'on':''}`} onClick={()=>setAutoInject(v=>!v)}>
            <span className="box"><DIcon name="check" size={12} /></span>
            완료 시 보고 자동 주입
          </label>
          <button className="btn-ghost" onClick={onClose}>취소</button>
          <button className="btn-primary" disabled={!canDelegate} onClick={submit}
            style={!canDelegate?{ opacity:.5, cursor:'default' }:{}}>
            <DIcon name="bolt" size={14} /> 위임 보내기
          </button>
        </div>
      </div>
    </div>
  );
}

/* ---------- B · NoIdleWorkerModal ---------- */
function NoIdleWorkerModal({ workers, onClose, onCreate }){
  const busy = workers.filter(w=>w.status==='busy');
  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" style={{ width:380 }} onClick={e=>e.stopPropagation()}>
        <div className="modal-h">
          <span className="lbl">Workers</span>
          <span style={{ color:'var(--txt-0)', fontWeight:600, fontSize:13 }}>유휴 워커 없음 (No idle worker)</span>
          <button className="x" onClick={onClose}><DIcon name="x" size={16} /></button>
        </div>
        <div className="modal-b">
          <p style={{ fontSize:12.5, color:'var(--txt-2)', lineHeight:1.6 }}>
            현재 모든 워커가 사용 중입니다. 새 워커를 생성할까요?
          </p>
          {busy.length>0 && (
            <div className="busy-list">
              {busy.map(w=>{
                const a = DAGENTS[w.agentId];
                return (
                  <div className="busy-item" key={w.id}>
                    <div className="wk-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
                    <span className="nm">{w.name} · {a.name}</span>
                    <span className="st-chip busy"><span className="spin" style={{ width:9, height:9, borderWidth:1.5 }} /> 사용 중</span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
        <div className="modal-f">
          <button className="btn-ghost" onClick={onClose}>닫기</button>
          <button className="btn-primary" onClick={onCreate}><DIcon name="plus" size={14} /> 생성</button>
        </div>
      </div>
    </div>
  );
}

/* ---------- C · DelegationCard (inline) ---------- */
function DelegationCard({ del, onPaste, onRedelegate, onOpenWorker }){
  const [open, setOpen] = React.useState(del.status==='ready' || del.status==='error');
  const a = DAGENTS[del.agentId];
  const live = del.status==='running' || del.status==='sent';
  const ST = { sent:'전송됨', running:'실행 중', ready:'완료', error:'실패' };
  return (
    <div className={`dcard ${live?'live':''}`}>
      <div className="dcard-h" onClick={()=>setOpen(o=>!o)}>
        <div className="dcard-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
        <div className="dcard-ti">
          <div className="dcard-t">워커 <b style={{ color:a.tint }}>{del.workerName}</b> · {del.model}에 위임</div>
          <div className="dcard-sub">{a.cli}{del.tr && del.tr.on ? ` · ${trLabel(del.tr)}` : ''}</div>
        </div>
        <span className={`dcard-st ${del.status}`}>
          {del.status==='running' && <span className="spin" style={{ width:9, height:9, borderWidth:1.5 }} />}
          {del.status==='ready' && <DIcon name="check" size={11} />}
          {del.status==='error' && <DIcon name="alert" size={11} />}
          {ST[del.status]}
        </span>
        <DIcon name={open?'chevup':'chevdown'} size={15} style={{ stroke:'var(--txt-2)' }} />
      </div>
      {open && (
        <div className="dcard-body">
          <div className="dcard-label">
            <DIcon name="send" size={11} /> 위임 프롬프트
            {del.tr && del.tr.on && <span className="dcard-tr"><DIcon name="translate" size={10} /> {trLabel(del.tr)}</span>}
          </div>
          <div className="dcard-prompt">{del.prompt}</div>

          {del.status==='ready' && (
            <>
              <div className="dcard-label" style={{ marginTop:13 }}><DIcon name="inbox" size={11} /> 보고 미리보기 (Report)</div>
              <div className="dcard-report">{del.report}</div>
            </>
          )}
          {del.status==='error' && (
            <>
              <div className="dcard-label" style={{ marginTop:13 }}><DIcon name="alert" size={11} /> 오류 (Error)</div>
              <div className="dcard-err">{del.error}</div>
            </>
          )}

          <div className="dcard-foot">
            <button className="dcard-link" onClick={()=>onOpenWorker && onOpenWorker(del)}>
              <DIcon name="ide" size={13} /> 워커 세션 열기
            </button>
            <span className="sp" />
            {del.status==='ready' && (
              <button className="fc-btn" onClick={()=>onPaste && onPaste(del.id)}>
                <DIcon name="copy" size={13} /> 보고 붙여넣기
              </button>
            )}
            {del.status==='error' && (
              <button className="btn-ghost" style={{ padding:'7px 13px' }} onClick={()=>onRedelegate && onRedelegate(del)}>
                <DIcon name="refresh" size={13} style={{ verticalAlign:'-2px' }} /> 다시 위임
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/* ---------- ReportInbox (near composer) ---------- */
function ReportInbox({ ready, onMerge }){
  if (!ready || ready.length===0) return null;
  return (
    <div className="rib">
      <span className="rib-ic"><DIcon name="inbox" size={17} /></span>
      <span className="rib-l"><b>보고 수신함</b> — 워커 보고 {ready.length}건이 도착했습니다</span>
      <span className="rib-count">{ready.length} READY</span>
      <span className="sp" />
      {ready.length>=2 && (
        <button className="rib-btn" onClick={onMerge}><DIcon name="layers" size={14} /> 합쳐 붙여넣기 ({ready.length}건)</button>
      )}
    </div>
  );
}

Object.assign(window, { WorkerAssignModal, NoIdleWorkerModal, DelegationCard, ReportInbox });
