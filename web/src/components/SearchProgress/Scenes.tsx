/** Animated illustration for each pipeline step, drawn with the theme CSS variables. */
function Understand() {
  return (
    <svg viewBox="0 0 160 110" className="scene" aria-hidden="true">
      <rect className="sc-card" x="22" y="22" width="116" height="66" rx="8" />
      <rect className="sc-ln sc-ln1" x="36" y="38" width="70" height="6" rx="3" />
      <rect className="sc-ln sc-ln2" x="36" y="52" width="88" height="6" rx="3" />
      <rect className="sc-ln sc-ln3" x="36" y="66" width="50" height="6" rx="3" />
      <g className="sc-lens">
        <circle className="sc-stroke" cx="0" cy="0" r="14" />
        <line className="sc-stroke" x1="10" y1="10" x2="22" y2="22" />
      </g>
      <path className="sc-spark sc-spark1" d="M136 14l2 6 6 2-6 2-2 6-2-6-6-2 6-2z" />
      <path className="sc-spark sc-spark2" d="M18 84l1.5 4.5 4.5 1.5-4.5 1.5-1.5 4.5-1.5-4.5-4.5-1.5 4.5-1.5z" />
    </svg>
  );
}

function Search() {
  return (
    <svg viewBox="0 0 160 110" className="scene" aria-hidden="true">
      <circle className="sc-ring sc-ring1" cx="80" cy="62" r="22" />
      <circle className="sc-ring sc-ring2" cx="80" cy="62" r="22" />
      <polygon className="sc-roof" points="80,16 134,40 26,40" />
      <rect className="sc-roof2" x="32" y="40" width="96" height="6" />
      {[40, 62, 84, 106].map((x) => (
        <rect key={x} className="sc-card" x={x} y="48" width="12" height="36" />
      ))}
      <rect className="sc-roof2" x="26" y="84" width="108" height="8" />
      <rect className="sc-book sc-fly1" x="74" y="62" width="9" height="13" rx="1.5" />
      <rect className="sc-book sc-fly2" x="74" y="62" width="9" height="13" rx="1.5" />
      <rect className="sc-book sc-fly3" x="74" y="62" width="9" height="13" rx="1.5" />
    </svg>
  );
}

function Match() {
  const left = [24, 50, 76];
  const right = [24, 50, 76];
  const links = [
    [0, 1],
    [1, 0],
    [2, 2],
  ];
  return (
    <svg viewBox="0 0 160 110" className="scene" aria-hidden="true">
      {left.map((y) => (
        <rect key={y} className="sc-card" x="10" y={y} width="42" height="18" rx="5" />
      ))}
      {right.map((y, i) => (
        <g key={y}>
          <rect className="sc-spine" x="108" y={y} width="42" height="18" rx="3" style={{ animationDelay: `${i * 0.3}s` }} />
          <rect className="sc-band" x="112" y={y + 4} width="3" height="10" rx="1.5" />
        </g>
      ))}
      {links.map(([a, b], i) => (
        <path
          key={i}
          className={i === 1 ? "sc-link sc-link-best" : "sc-link"}
          pathLength={1}
          d={`M52 ${left[a] + 9} C80 ${left[a] + 9}, 80 ${right[b] + 9}, 108 ${right[b] + 9}`}
          style={{ animationDelay: `${i * 0.5}s` }}
        />
      ))}
      <g className="sc-check">
        <circle cx="146" cy="30" r="8" />
        <path d="M142 30l3 3 5-6" />
      </g>
    </svg>
  );
}

function Write() {
  return (
    <svg viewBox="0 0 160 110" className="scene" aria-hidden="true">
      <rect className="sc-card" x="22" y="14" width="116" height="82" rx="8" />
      <rect className="sc-grow sc-grow1" x="36" y="34" width="88" height="6" rx="3" />
      <rect className="sc-grow sc-grow2" x="36" y="50" width="62" height="6" rx="3" />
      <rect className="sc-grow sc-grow3" x="36" y="66" width="76" height="6" rx="3" />
      <g className="sc-quill">
        <path className="sc-feather" d="M0 0L16-18Q24-26 30-22Q28-10 10 6Z" />
        <path className="sc-stroke sc-thin" d="M0 0L22-20" />
      </g>
    </svg>
  );
}

export function Scene({ step, count }: { step: number; count: number }) {
  const i = Math.min(step, count - 1);
  return (
    <span className="scene-wrap" key={i}>
      {i === 0 && <Understand />}
      {i === 1 && <Search />}
      {i === 2 && <Match />}
      {i === 3 && <Write />}
    </span>
  );
}
