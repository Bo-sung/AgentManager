/* ============================================================
   am-components.jsx — shared atoms
   ============================================================ */
const { Icon, AGENTS } = window.AM;

const STATUS_LABEL = { running:'Running', waiting:'Awaiting input', done:'Completed', error:'Failed', idle:'Idle' };
const STATUS_COLOR = { running:'var(--run)', waiting:'var(--warn)', done:'var(--ok)', error:'var(--err)', idle:'var(--txt-3)' };

function fmtElapsed(ms){
  const s = Math.max(0, Math.floor(ms/1000));
  const h = Math.floor(s/3600), m = Math.floor((s%3600)/60), ss = s%60;
  const pad = n => String(n).padStart(2,'0');
  return h>0 ? `${pad(h)}:${pad(m)}:${pad(ss)}` : `${pad(m)}:${pad(ss)}`;
}
function relTime(ms){
  const s = Math.floor((Date.now()-ms)/1000);
  if (s<60) return `${s}s ago`;
  const m = Math.floor(s/60);
  if (m<60) return `${m}m ago`;
  const h = Math.floor(m/60);
  if (h<24) return `${h}h ago`;
  return `${Math.floor(h/24)}d ago`;
}

function StatusDot({ status }){
  if (status==='running') return <div className="spin" title="Running" />;
  return <div className={`dot ${status}`} title={STATUS_LABEL[status]} />;
}

function Spark(){
  return (
    <div className="spark" aria-hidden="true">
      {[0,1,2,3,4].map(i=>(
        <i key={i} style={{ animationDelay:`${i*0.13}s` }} />
      ))}
    </div>
  );
}

function AgentBadge({ agentId, size }){
  const a = AGENTS[agentId];
  const s = size || 26;
  return (
    <div className="ses-badge" style={{ width:s, height:s, flexBasis:s, fontSize: s>30?12:10,
      color:a.tint, borderColor:a.line, background:a.soft }}>
      {a.badge}
    </div>
  );
}

/* live-updating elapsed timer that re-renders every second while active */
function useTick(active){
  const [,force] = React.useState(0);
  React.useEffect(()=>{
    if(!active) return;
    const id = setInterval(()=>force(x=>x+1), 1000);
    return ()=>clearInterval(id);
  },[active]);
}

Object.assign(window, {
  STATUS_LABEL, STATUS_COLOR, fmtElapsed, relTime,
  StatusDot, Spark, AgentBadge, useTick,
});
