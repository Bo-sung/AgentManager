/* ============================================================
   am-chat.jsx — main pane: tabbar, status strip, transcript, composer
   ============================================================ */
const { Icon: CIcon, AGENTS: CAGENTS } = window.AM;

/* ---- inline markdown-ish (**bold**, `code`) ---- */
function inline(text){
  const out = [];
  const re = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let last = 0, m, k = 0;
  while ((m = re.exec(text))){
    if (m.index > last) out.push(text.slice(last, m.index));
    const tok = m[0];
    if (tok.startsWith('**')) out.push(<strong key={k++}>{tok.slice(2,-2)}</strong>);
    else out.push(<code key={k++}>{tok.slice(1,-1)}</code>);
    last = m.index + tok.length;
  }
  if (last < text.length) out.push(text.slice(last));
  return out;
}
function Prose({ items }){
  return (
    <div className="prose">
      {items.map((it,i)=>{
        if (typeof it === 'string') return <p key={i}>{inline(it)}</p>;
        if (it.ul) return <ul key={i}>{it.ul.map((li,j)=><li key={j}>{inline(li)}</li>)}</ul>;
        return null;
      })}
    </div>
  );
}

/* ---- tool call card ---- */
const TOOL_META = {
  read: { icon:'eye',      kind:'READ' },
  edit: { icon:'edit',     kind:'EDIT' },
  cmd:  { icon:'terminal', kind:'RUN'  },
};
function ToolCard({ b }){
  const [open, setOpen] = React.useState(false);
  const meta = TOOL_META[b.kind] || TOOL_META.cmd;
  return (
    <div className="tool">
      <div className="tool-h" onClick={()=>setOpen(o=>!o)}>
        <span className="tool-ico"><CIcon name={meta.icon} size={15} /></span>
        <span className="tool-kind">{meta.kind}</span>
        <span className="tool-name">{b.name}</span>
        <span className="tool-stat">{b.stat}<CIcon name={open?'chevup':'chevdown'} size={13} /></span>
      </div>
      {open && <div className="tool-body">{b.body}</div>}
    </div>
  );
}

/* ---- file change / review card ---- */
function FileChange({ b, onReview }){
  return (
    <div className="fc">
      <span className="fc-ico"><CIcon name="layers" size={17} /></span>
      <div>
        <div className="fc-txt">
          {b.files.split('·')[0].trim()} changed&nbsp;
          <span className="fc-add">+{b.add}</span>&nbsp;<span className="fc-del">−{b.del}</span>
        </div>
        <div className="fc-files">{b.files.split('·').slice(1).join('·').trim()}</div>
      </div>
      <span className="fc-spacer" />
      <button className="fc-btn" onClick={onReview}><CIcon name="eye" size={14} /> Review</button>
    </div>
  );
}

/* ---- approval prompt ---- */
function Approve({ b, onApprove, onReject }){
  return (
    <div className="approve">
      <div className="approve-h"><CIcon name="warn" size={14} /> {b.title}</div>
      <div className="approve-b">
        {b.text}
        <div className="approve-cmd">$ {b.cmd}</div>
        <div className="approve-btns">
          <button className="btn-approve" onClick={onApprove}><CIcon name="check" size={14} style={{verticalAlign:'-2px'}} /> Approve &amp; run</button>
          <button className="btn-reject" onClick={onReject}>Reject</button>
        </div>
      </div>
    </div>
  );
}

/* ---- error block ---- */
function ErrBlock({ b }){
  return (
    <div className="errblk">
      <div className="errblk-h"><CIcon name="alert" size={14} /> {b.title}</div>
      <div className="errblk-b">{b.body}</div>
    </div>
  );
}

/* ---- message action toolbar ---- */
function MsgActions(){
  return (
    <div className="msg-actions">
      <button title="Continue in IDE"><CIcon name="ide" size={15} /></button>
      <button title="Copy"><CIcon name="copy" size={15} /></button>
      <button title="Rerun"><CIcon name="refresh" size={15} /></button>
      <button title="Good"><CIcon name="thumbup" size={15} /></button>
      <button title="Bad"><CIcon name="thumbdown" size={15} /></button>
    </div>
  );
}

/* ---- group blocks into user / agent runs ---- */
function groupBlocks(blocks){
  const groups = [];
  blocks.forEach(b=>{
    if (b.t==='user'){ groups.push({ type:'user', block:b }); }
    else {
      const last = groups[groups.length-1];
      if (last && last.type==='agent') last.blocks.push(b);
      else groups.push({ type:'agent', blocks:[b] });
    }
  });
  return groups;
}

/* ---- one agent run ---- */
function AgentRun({ session, blocks, streamShown, onReview, onApprove, onReject }){
  const a = CAGENTS[session.agentId];
  return (
    <div className="msg-agent">
      <div className="ag-rail">
        <div className="ag-mark" style={{ color:a.tint, borderColor:a.line, background:a.soft }}>{a.badge}</div>
        <div className="ag-line" />
      </div>
      <div className="ag-body">
        <div className="ag-name">
          <span className="n" style={{ color:a.tint }}>{a.name}</span>
          <span className="m">/ {session.model}</span>
        </div>
        {blocks.map((b,i)=>{
          if (b.t==='agent') return <Prose key={i} items={b.prose} />;
          if (b.t==='tool')  return <ToolCard key={i} b={b} />;
          if (b.t==='fc')    return <FileChange key={i} b={b} onReview={onReview} />;
          if (b.t==='approve') return <Approve key={i} b={b} onApprove={onApprove} onReject={onReject} />;
          if (b.t==='err')   return <ErrBlock key={i} b={b} />;
          if (b.t==='stream'){
            const shown = b.full.slice(0, streamShown);
            const done = streamShown >= b.full.length;
            return (
              <div key={i}>
                {b.prose_pre && <Prose items={b.prose_pre} />}
                <div className="prose"><p>{shown}{!done && <span className="cursor" />}</p></div>
              </div>
            );
          }
          return null;
        })}
        {session.status==='running' && (
          <div className="working-line"><div className="spin" /> {session.activity}…</div>
        )}
      </div>
    </div>
  );
}

/* ---- model dropdown ---- */
function ModelMenu({ agent, value, onPick, onClose }){
  React.useEffect(()=>{
    const h = ()=>onClose();
    setTimeout(()=>window.addEventListener('click', h), 0);
    return ()=>window.removeEventListener('click', h);
  },[]);
  return (
    <div className="menu" style={{ bottom:'46px', left:0, minWidth:230 }} onClick={e=>e.stopPropagation()}>
      <div className="lbl" style={{ padding:'6px 10px 8px' }}>{agent.name} · model</div>
      {agent.models.map(m=>(
        <div key={m} className={`menu-item ${m===value?'sel':''}`} onClick={()=>{ onPick(m); onClose(); }}>
          <CIcon name={m===value?'check':'spark'} size={14} />
          <span>{m}</span>
          {m===value && <span className="mn-code">ACTIVE</span>}
        </div>
      ))}
    </div>
  );
}

/* ---- composer ---- */
function Composer({ session, value, onChange, onSend, onModel }){
  const [focus, setFocus] = React.useState(false);
  const [menu, setMenu] = React.useState(false);
  const a = CAGENTS[session.agentId];
  const ref = React.useRef();
  const grow = el => { el.style.height='auto'; el.style.height = Math.min(el.scrollHeight,160)+'px'; };
  const send = ()=>{ if(value.trim()){ onSend(); if(ref.current){ ref.current.style.height='46px'; } } };
  return (
    <div className="composer">
      <div className={`comp-box ${focus?'focus':''}`}
        style={focus?{ borderColor:a.line, boxShadow:`0 0 0 3px ${a.soft}` }:{}}>
        <textarea
          ref={ref}
          className="comp-input"
          placeholder={`Message ${a.name}…  @ to mention, / for actions`}
          value={value}
          onFocus={()=>setFocus(true)} onBlur={()=>setFocus(false)}
          onChange={e=>{ onChange(e.target.value); grow(e.target); }}
          onKeyDown={e=>{ if(e.key==='Enter' && !e.shiftKey){ e.preventDefault(); send(); } }}
        />
        <div className="comp-bar">
          <button className="c-ico" title="Attach"><CIcon name="plusbox" size={19} /></button>
          <div className="comp-pill" title="Worktree"><CIcon name="branch" size={13} /> Worktree · {session.branch.split('/').pop()}</div>
          <div style={{ position:'relative' }}>
            <div className="comp-pill model" onClick={e=>{ e.stopPropagation(); setMenu(m=>!m); }}>
              <CIcon name="spark" size={13} /> <b style={{ color:a.tint }}>{session.model}</b> <CIcon name="chevup" size={12} />
            </div>
            {menu && <ModelMenu agent={a} value={session.model} onPick={onModel} onClose={()=>setMenu(false)} />}
          </div>
          <span className="comp-spacer" />
          <button className="c-ico" title="Dictate"><CIcon name="mic" size={18} /></button>
          <button className="send" disabled={!value.trim()} onClick={send}
            style={value.trim()?{ background:a.tint }:{}}><CIcon name="send" size={17} /></button>
        </div>
      </div>
      <div className="comp-hint">
        <span>ENTER to send · SHIFT+ENTER for newline</span>
        <span>{a.cli} · {session.branch}</span>
      </div>
    </div>
  );
}

/* ---- status strip ---- */
function StatusStrip({ session, tokens }){
  useTick(session.status==='running' || session.status==='waiting');
  const live = session.status==='running' || session.status==='waiting';
  const elapsed = fmtElapsed(Date.now()-session.startedAt);
  return (
    <div className="strip">
      <div className="st"><StatusDot status={session.status} /> <span className={live?'strip-run':'v'} style={live?{color:STATUS_COLOR[session.status]}:{}}>{STATUS_LABEL[session.status]}</span></div>
      <div className="div" />
      <div className="st"><b>AGENT</b> <span className="v">{CAGENTS[session.agentId].name}</span></div>
      <div className="div" />
      <div className="st"><b>MODEL</b> <span className="v">{session.model}</span></div>
      <div className="strip-right">
        <div className="st"><b>TKN</b> <span className="v">{tokens}</span></div>
        <div className="div" />
        <div className="st"><b>ELAPSED</b> <span className="v" style={live?{color:'var(--accent)'}:{}}>{elapsed}</span></div>
      </div>
    </div>
  );
}

/* ---- tab bar ---- */
function TabBar({ session, onReview, reviewOpen, onToggleReview }){
  return (
    <div className="tabbar">
      <div className="crumb">
        <span className="proj">{session.project}</span>
        <span className="sep">›</span>
        <span className="ttl">{session.title}</span>
      </div>
      <div className="branch-chip"><CIcon name="branch" size={12} /> {session.branch}</div>
      <span className="tab-spacer" />
      <button className="tabbtn accent"><CIcon name="ide" size={14} /> Open IDE</button>
      <button className={`icobtn ${reviewOpen?'on':''}`} title="Diff review" onClick={onToggleReview}><CIcon name="panel" size={16} /></button>
    </div>
  );
}

/* ---- whole chat pane ---- */
function ChatPane({ session, draft, onDraft, onSend, onModel, streamShown, tokens, onReview, reviewOpen, onToggleReview, onApprove, onReject }){
  const groups = groupBlocks(session.blocks);
  const scrollRef = React.useRef();
  React.useEffect(()=>{
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  },[session.id, streamShown, session.blocks.length]);

  return (
    <div className="main">
      <TabBar session={session} onReview={()=>onReview(session)} reviewOpen={reviewOpen} onToggleReview={onToggleReview} />
      <StatusStrip session={session} tokens={tokens} />
      <div className="scroll" ref={scrollRef}>
        <div className="thread">
          {groups.map((g,i)=>{
            if (g.type==='user') return (
              <div className="msg-user" key={i}>
                <div className="bubble"><div className="b-head">You → {CAGENTS[session.agentId].cli}</div>{g.block.text}</div>
              </div>
            );
            return (
              <div key={i}>
                <AgentRun session={session} blocks={g.blocks} streamShown={streamShown}
                  onReview={()=>onReview(session)} onApprove={()=>onApprove(session.id)} onReject={()=>onReject(session.id)} />
                {session.status!=='running' && i===groups.length-1 && <MsgActions />}
              </div>
            );
          })}
        </div>
      </div>
      <Composer session={session} value={draft} onChange={onDraft} onSend={onSend} onModel={onModel} />
    </div>
  );
}

window.ChatPane = ChatPane;
