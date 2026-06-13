/* ============================================================
   am-sidebar.jsx — left rail: nav + live session list
   ============================================================ */
const { Icon: SIcon, AGENTS: SAGENTS, NAV: SNAV, PROJECT: SPROJECT } = window.AM;

function SessionRow({ s, active, onClick }){
  useTick(s.status==='running' || s.status==='waiting');
  const a = SAGENTS[s.agentId];
  const live = s.status==='running' || s.status==='waiting';
  const dim = s.status==='done' || s.status==='error' || s.status==='idle';
  return (
    <div className={`ses ${active?'active':''} ${dim?'dim':''}`} onClick={onClick}>
      <AgentBadge agentId={s.agentId} />
      <div className="ses-main">
        <div className="ses-title">{s.title}</div>
        <div className="ses-meta">
          <span className="ses-branch"><SIcon name="branch" size={11} /><span>{s.branch}</span></span>
          <span className="ses-time">{relTime(s.startedAt)}</span>
        </div>
        {s.status==='running' && (
          <div className="ses-act"><Spark /><span>{s.activity}</span></div>
        )}
        {s.status==='waiting' && (
          <div className="ses-act" style={{ color:'var(--warn)' }}>
            <SIcon name="alert" size={11} /><span>{s.activity}</span>
          </div>
        )}
        {s.status==='error' && (
          <div className="ses-act" style={{ color:'var(--err)' }}>
            <SIcon name="alert" size={11} /><span>{s.activity}</span>
          </div>
        )}
      </div>
      <div style={{ paddingTop:2 }}><StatusDot status={s.status} /></div>
    </div>
  );
}

function Sidebar({ sessions, activeId, onSelect, nav, onNav, onNew }){
  const running = sessions.filter(s=>s.status==='running' || s.status==='waiting');
  const rest = sessions.filter(s=>!(s.status==='running' || s.status==='waiting'));

  return (
    <div className="rail">
      <div className="rail-top">
        <button className="btn-new" onClick={onNew}>
          <SIcon name="plus" size={15} /> New Agent
        </button>
      </div>

      <div className="rail-nav">
        {SNAV.map(n=>(
          <div key={n.id} className={`nav-item ${nav===n.id?'active':''}`} onClick={()=>onNav(n.id)}>
            <SIcon name={n.icon} size={16} />
            <span>{n.label}</span>
            {n.ct && <span className="ct">{n.ct}</span>}
          </div>
        ))}
      </div>

      <div className="rail-scroll">
        <div className="sec-head">
          <span className="lbl">Active</span>
          <span className="cnt">{running.length}</span>
          <span className="rule" />
          <div className="dot running" style={{ position:'static' }} />
        </div>
        {running.map(s=>(
          <SessionRow key={s.id} s={s} active={s.id===activeId} onClick={()=>onSelect(s.id)} />
        ))}

        <div className="sec-head">
          <span className="lbl">Project · {SPROJECT}</span>
          <span className="cnt">{rest.length}</span>
          <span className="rule" />
        </div>
        {rest.map(s=>(
          <SessionRow key={s.id} s={s} active={s.id===activeId} onClick={()=>onSelect(s.id)} />
        ))}
      </div>

      <div className="rail-foot">
        <div className="user-chip" style={{ flex:1 }}>
          <div className="avatar">JK</div>
          <div style={{ minWidth:0 }}>
            <div style={{ fontSize:12, color:'var(--txt-0)', fontWeight:500 }}>jihoon.k</div>
            <div className="lbl" style={{ fontSize:9, letterSpacing:'.1em' }}>3 workers online</div>
          </div>
        </div>
        <div className={`nav-item ${nav==='settings'?'active':''}`} style={{ flex:'none', width:34, justifyContent:'center' }} onClick={()=>onNav('settings')} title="Settings">
          <SIcon name="settings" size={17} />
        </div>
      </div>
    </div>
  );
}

window.Sidebar = Sidebar;
