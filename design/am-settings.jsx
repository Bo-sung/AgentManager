/* ============================================================
   am-settings.jsx — Settings view (runtimes, orchestration,
   permissions, appearance)
   ============================================================ */
const { Icon: SetIcon, AGENT_LIST: SetAGENTS } = window.AM;

function Toggle({ on, onChange }){
  return <button className={`tgl ${on?'on':''}`} onClick={()=>onChange(!on)} aria-pressed={on}><span className="knob" /></button>;
}
function Seg({ options, value, onChange, warn }){
  return (
    <div className="seg">
      {options.map(o=>(
        <button key={o.v} className={`${value===o.v?'sel':''} ${warn&&o.warn?'warn':''}`} onClick={()=>onChange(o.v)}>{o.label}</button>
      ))}
    </div>
  );
}
function Stepper({ value, min, max, onChange }){
  return (
    <div className="stepper">
      <button onClick={()=>onChange(Math.max(min, value-1))}>−</button>
      <span className="val">{value}</span>
      <button onClick={()=>onChange(Math.min(max, value+1))}>+</button>
    </div>
  );
}
function MiniSelect({ value, options, onChange }){
  const [open, setOpen] = React.useState(false);
  React.useEffect(()=>{
    if(!open) return;
    const h = ()=>setOpen(false);
    setTimeout(()=>window.addEventListener('click', h),0);
    return ()=>window.removeEventListener('click', h);
  },[open]);
  return (
    <div className="set-select" onClick={e=>{ e.stopPropagation(); setOpen(o=>!o); }}>
      <SetIcon name="spark" size={13} />
      <span className="cv">{value}</span>
      <SetIcon name="chevdown" size={13} />
      {open && (
        <div className="menu" style={{ top:'40px', left:0, right:0 }} onClick={e=>e.stopPropagation()}>
          {options.map(o=>(
            <div key={o} className={`menu-item ${o===value?'sel':''}`} onClick={()=>{ onChange(o); setOpen(false); }}>
              <SetIcon name={o===value?'check':'spark'} size={13} /><span>{o}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Row({ title, desc, children }){
  return (
    <div className="set-row">
      <div className="set-meta">
        <div className="set-t">{title}</div>
        {desc && <div className="set-d">{desc}</div>}
      </div>
      <div className="set-ctl">{children}</div>
    </div>
  );
}
function SecHead({ num, children }){
  return (
    <div className="set-sec-head">
      <span className="num">{num}</span>
      <span className="h">{children}</span>
      <span className="rule" />
    </div>
  );
}

const ACCENTS = [
  { name:'Ember',  accent:'#ff5a2c', bright:'#ff7048' },
  { name:'Amber',  accent:'#f5b531', bright:'#ffc94d' },
  { name:'Teal',   accent:'#2bd4c4', bright:'#4fe6d7' },
  { name:'Azure',  accent:'#5b8cff', bright:'#7ba6ff' },
  { name:'Violet', accent:'#a87bff', bright:'#c0a0ff' },
];

const DEFAULT_PATHS = { cc:'/usr/local/bin/claude', gx:'/usr/local/bin/codex', ag:'/opt/antigravity/bin/antigravity' };
const RT_VERSIONS = { cc:'v2.4.1', gx:'v1.9.0', ag:'v0.7.3' };

/* subscription account data per runtime */
const SetAGMAP = window.AM.AGENTS;
const ACCOUNT_INFO = {
  cc: { provider:'Anthropic', email:'jihoon.k@gmail.com', plans:['Claude Pro','Claude Max 5×','Claude Max 20×'], plan:'Claude Max 20×', renews:'renews May 1', used:312, limit:500, unit:'k tokens' },
  gx: { provider:'OpenAI',    email:'jihoon.k@gmail.com', plans:['ChatGPT Plus','ChatGPT Pro','ChatGPT Team'], plan:'ChatGPT Pro', renews:'renews May 1', used:118, limit:300, unit:'k tokens' },
  ag: { provider:'Google',    email:'jihoon.k@gmail.com', plans:['Google AI Pro','Google AI Ultra'], plan:'Google AI Ultra', renews:'renews May 1', used:46, limit:200, unit:'k tokens' },
};
/* preset auth state: cc linked via subscription, gx via API key, ag subscription not-yet-linked */
const AUTH_PRESET = { cc:{ method:'subscription', linked:true }, gx:{ method:'api', linked:false }, ag:{ method:'subscription', linked:false } };

function LinkedAccount({ agentId, cfg, onPlan, onUnlink }){
  const info = ACCOUNT_INFO[agentId];
  const a = SetAGMAP[agentId];
  const pct = Math.round(info.used/info.limit*100);
  return (
    <div className="acct-card">
      <div className="acct-av" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
      <div className="acct-info">
        <div className="acct-row1">
          <b>{info.email}</b>
          <span className="ok-chip"><SetIcon name="check" size={10} /> Linked</span>
        </div>
        <div className="acct-row2">{info.provider} · {info.renews} · {info.used}{info.unit} used this cycle</div>
        <div className="acct-usage"><div className="acct-bar"><i style={{ width:pct+'%' }} /></div></div>
      </div>
      <div className="acct-actions">
        <MiniSelect value={cfg.plan||info.plan} options={info.plans} onChange={onPlan} />
        <button className="unlink-btn" onClick={onUnlink}>Unlink</button>
      </div>
    </div>
  );
}

function EmptyAccount({ agentId, onLink }){
  const info = ACCOUNT_INFO[agentId];
  const a = SetAGMAP[agentId];
  return (
    <div className="acct-empty">
      <div className="acct-av" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
      <div className="txt">Sign in with your {info.provider} subscription to run this agent on your plan's included usage instead of metered API billing.</div>
      <button className="acct-link-btn" onClick={onLink}><SetIcon name="bolt" size={14} /> Link {info.provider} account</button>
    </div>
  );
}

function LinkAccountModal({ agentId, onClose, onAuthorize }){
  const info = ACCOUNT_INFO[agentId];
  const a = SetAGMAP[agentId];
  return (
    <div className="overlay" onClick={onClose}>
      <div className="modal" style={{ width:480 }} onClick={e=>e.stopPropagation()}>
        <div className="modal-h">
          <span className="lbl">Link account</span>
          <span style={{ color:'var(--txt-0)', fontWeight:600, fontSize:13 }}>{info.provider}</span>
          <button className="x" onClick={onClose}><SetIcon name="x" size={16} /></button>
        </div>
        <div className="modal-b">
          <div className="prov-head">
            <div className="prov-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
            <div>
              <div className="pn">Authorize AgentManager</div>
              <div className="ps">{a.name} · {info.provider} subscription</div>
            </div>
          </div>
          <span className="lbl">This will allow AgentManager to</span>
          <div className="scope-list">
            <div className="scope-item"><SetIcon name="check" size={15} /> Run agents against your plan's included usage</div>
            <div className="scope-item"><SetIcon name="check" size={15} /> Read your subscription tier and usage limits</div>
            <div className="scope-item no"><SetIcon name="x" size={15} /> No access to billing or payment methods</div>
          </div>
          <div className="oauth-note">
            <SetIcon name="alert" size={13} />
            <span>You'll be redirected to {info.provider} to sign in. AgentManager never sees your password — only a scoped token stored in the OS keychain.</span>
          </div>
        </div>
        <div className="modal-f">
          <button className="btn-ghost" onClick={onClose}>Cancel</button>
          <button className="btn-primary" onClick={()=>onAuthorize(agentId)}><SetIcon name="bolt" size={14} /> Authorize {info.provider}</button>
        </div>
      </div>
    </div>
  );
}

function SettingsView({ onClose }){
  const [rt, setRt] = React.useState(()=>{
    const o = {}; SetAGENTS.forEach(a=>{ o[a.id] = { enabled:true, path:DEFAULT_PATHS[a.id], model:a.model, key:'sk-••••••••••••'+a.id.toUpperCase(), plan:ACCOUNT_INFO[a.id].plan, ...AUTH_PRESET[a.id] }; });
    return o;
  });
  const [linkTarget, setLinkTarget] = React.useState(null);
  const [maxConcurrent, setMaxConcurrent] = React.useState(4);
  const [autoStart, setAutoStart] = React.useState(true);
  const [worktreeBase, setWorktreeBase] = React.useState('.worktrees/');
  const [streamLogs, setStreamLogs] = React.useState(true);

  const [approval, setApproval] = React.useState('ask');
  const [allowDestructive, setAllowDestructive] = React.useState(false);
  const [network, setNetwork] = React.useState(true);
  const [sandbox, setSandbox] = React.useState(true);

  const [accent, setAccent] = React.useState('#ff5a2c');
  const [density, setDensity] = React.useState('comfortable');
  const [telemetry, setTelemetry] = React.useState(false);

  const setAgent = (id, patch)=> setRt(r=>({ ...r, [id]:{ ...r[id], ...patch } }));

  const pickAccent = (a)=>{
    setAccent(a.accent);
    const root = document.documentElement;
    root.style.setProperty('--accent', a.accent);
    root.style.setProperty('--accent-bright', a.bright);
    root.style.setProperty('--run', a.accent);
    const hex = a.accent.replace('#','');
    const r=parseInt(hex.slice(0,2),16), g=parseInt(hex.slice(2,4),16), b=parseInt(hex.slice(4,6),16);
    root.style.setProperty('--accent-dim', `rgba(${r},${g},${b},.14)`);
    root.style.setProperty('--accent-line', `rgba(${r},${g},${b},.40)`);
  };

  const [sec, setSec] = React.useState('runtimes');
  const scrollRef = React.useRef();
  const goto = (id)=>{
    setSec(id);
    const el = document.getElementById('sec-'+id);
    const cont = scrollRef.current;
    if (!el || !cont) return;
    const target = el.getBoundingClientRect().top - cont.getBoundingClientRect().top + cont.scrollTop - 56;
    const start = cont.scrollTop, dist = target - start, t0 = Date.now(), dur = 340;
    const iv = setInterval(()=>{
      const p = Math.min(1, (Date.now() - t0) / dur);
      const e = 1 - Math.pow(1 - p, 3);
      cont.scrollTop = start + dist * e;
      if (p >= 1) clearInterval(iv);
    }, 16);
  };

  /* highlight TOC section as it scrolls into view */
  const onScroll = ()=>{
    const cont = scrollRef.current; if(!cont) return;
    const ids = ['runtimes','orchestration','permissions','appearance'];
    const ct = cont.getBoundingClientRect().top + 80;
    let cur = ids[0];
    ids.forEach(id=>{
      const el = document.getElementById('sec-'+id);
      if (el && el.getBoundingClientRect().top <= ct) cur = id;
    });
    setSec(cur);
  };

  return (
    <div className="main">
      <div className="set-head">
        <SetIcon name="settings" size={16} style={{ color:'var(--accent)' }} />
        <span className="ttl">Settings</span>
        <span className="lbl">workspace · jihoon.k</span>
        <span style={{ flex:1 }} />
        <button className="icobtn" title="Close settings" onClick={onClose}><SetIcon name="x" size={15} /></button>
      </div>

      <div className="set-scroll" ref={scrollRef} onScroll={onScroll}>
        <div className="set-toc">
          {[['runtimes','Runtimes'],['orchestration','Orchestration'],['permissions','Permissions'],['appearance','Appearance']].map(([id,lbl])=>(
            <button key={id} className={sec===id?'on':''} onClick={()=>goto(id)}>{lbl}</button>
          ))}
        </div>

        <div className="set-page">
          {/* ---- Runtimes ---- */}
          <div className="set-section" id="sec-runtimes">
            <SecHead num="01">Agent Runtimes</SecHead>
            {SetAGENTS.map(a=>{
              const cfg = rt[a.id];
              return (
                <div className="set-card" key={a.id}>
                  <div className="rt-head">
                    <div className="rt-badge" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
                    <div style={{ flex:1 }}>
                      <div className="rt-name">{a.name}</div>
                      <div className="rt-sub">{a.cli} · {a.desc}</div>
                    </div>
                    <div className={`rt-ver ${cfg.enabled?'':'off'}`}>
                      <SetIcon name={cfg.enabled?'check':'x'} size={11} /> {cfg.enabled?`detected ${RT_VERSIONS[a.id]}`:'disabled'}
                    </div>
                    <Toggle on={cfg.enabled} onChange={v=>setAgent(a.id,{enabled:v})} />
                  </div>
                  {cfg.enabled && (
                    <>
                      <Row title="Authentication" desc="Run on your subscription plan, or bill per-token with an API key.">
                        <Seg value={cfg.method} onChange={m=>setAgent(a.id,{method:m})}
                          options={[{v:'subscription',label:'Subscription'},{v:'api',label:'API key'}]} />
                      </Row>
                      {cfg.method==='subscription' ? (
                        <div className="set-row col">
                          <div className="set-meta">
                            <div className="set-t">Subscription account</div>
                            <div className="set-d">{cfg.linked ? `Linked to your ${ACCOUNT_INFO[a.id].provider} subscription.` : `Not linked — sign in to use your ${ACCOUNT_INFO[a.id].provider} plan.`}</div>
                          </div>
                          {cfg.linked
                            ? <LinkedAccount agentId={a.id} cfg={cfg} onPlan={p=>setAgent(a.id,{plan:p})} onUnlink={()=>setAgent(a.id,{linked:false})} />
                            : <EmptyAccount agentId={a.id} onLink={()=>setLinkTarget(a.id)} />}
                        </div>
                      ) : (
                        <Row title="API credential" desc="Stored in the OS keychain — never written to the project.">
                          <input className="set-input key" type="password" value={cfg.key} onChange={e=>setAgent(a.id,{key:e.target.value})} spellCheck={false} />
                        </Row>
                      )}
                      <Row title="CLI binary" desc="Absolute path to the executable used to launch this runtime.">
                        <input className="set-input" value={cfg.path} onChange={e=>setAgent(a.id,{path:e.target.value})} spellCheck={false} />
                      </Row>
                      <Row title="Default model" desc="Used when a new worker is spawned with this runtime.">
                        <MiniSelect value={cfg.model} options={a.models} onChange={m=>setAgent(a.id,{model:m})} />
                      </Row>
                    </>
                  )}
                </div>
              );
            })}
          </div>

          {/* ---- Orchestration ---- */}
          <div className="set-section" id="sec-orchestration">
            <SecHead num="02">Orchestration</SecHead>
            <div className="set-card">
              <Row title="Max concurrent agents" desc="How many workers may run at once across all runtimes.">
                <Stepper value={maxConcurrent} min={1} max={12} onChange={setMaxConcurrent} />
              </Row>
              <Row title="Auto-start on launch" desc="Resume the previous session's running agents when AgentManager opens.">
                <Toggle on={autoStart} onChange={setAutoStart} />
              </Row>
              <Row title="Worktree base directory" desc={<>Each agent runs in an isolated git worktree under this path.</>}>
                <input className="set-input" value={worktreeBase} onChange={e=>setWorktreeBase(e.target.value)} spellCheck={false} style={{ width:220 }} />
              </Row>
              <Row title="Stream activity logs" desc="Show live tool calls and token counts in the session list.">
                <Toggle on={streamLogs} onChange={setStreamLogs} />
              </Row>
            </div>
          </div>

          {/* ---- Permissions ---- */}
          <div className="set-section" id="sec-permissions">
            <SecHead num="03">Permissions &amp; Safety</SecHead>
            <div className="set-card">
              <Row title="Approval policy" desc="When an agent wants to run a shell command or write outside its worktree.">
                <Seg value={approval} onChange={setApproval} warn
                  options={[{v:'ask',label:'Always ask'},{v:'safe',label:'Auto · safe'},{v:'yolo',label:'Auto · all',warn:true}]} />
              </Row>
              <Row title="Allow destructive operations" desc={<>Permit migrations, force-pushes, and <code>rm</code> without a per-action prompt.</>}>
                <Toggle on={allowDestructive} onChange={setAllowDestructive} />
              </Row>
              <Row title="Network access" desc="Allow agents to fetch packages and hit external APIs.">
                <Toggle on={network} onChange={setNetwork} />
              </Row>
              <Row title="Sandboxed filesystem" desc="Confine each agent's writes to its own worktree until you keep the diff.">
                <Toggle on={sandbox} onChange={setSandbox} />
              </Row>
            </div>
          </div>

          {/* ---- Appearance ---- */}
          <div className="set-section" id="sec-appearance">
            <SecHead num="04">Appearance</SecHead>
            <div className="set-card">
              <Row title="Accent color" desc="Applies live across the whole client.">
                <div className="sw-row">
                  {ACCENTS.map(a=>(
                    <div key={a.name} className={`sw ${accent===a.accent?'sel':''}`} style={{ background:a.accent }} title={a.name} onClick={()=>pickAccent(a)} />
                  ))}
                </div>
              </Row>
              <Row title="Density" desc="Spacing of the session list and transcript.">
                <Seg value={density} onChange={setDensity}
                  options={[{v:'compact',label:'Compact'},{v:'comfortable',label:'Comfortable'}]} />
              </Row>
              <Row title="Anonymous telemetry" desc="Share crash reports and usage metrics to improve the orchestrator.">
                <Toggle on={telemetry} onChange={setTelemetry} />
              </Row>
            </div>

            <div className="set-foot">
              <span className="lbl">AgentManager 1.4.0 · build 20260613 · 3 runtimes registered</span>
              <span style={{ flex:1 }} />
              <button className="danger-btn">Reset all sessions</button>
            </div>
          </div>
        </div>
      </div>
      {linkTarget && (
        <LinkAccountModal agentId={linkTarget} onClose={()=>setLinkTarget(null)}
          onAuthorize={(id)=>{ setAgent(id,{ linked:true, method:'subscription' }); setLinkTarget(null); }} />
      )}
    </div>
  );
}

window.SettingsView = SettingsView;
