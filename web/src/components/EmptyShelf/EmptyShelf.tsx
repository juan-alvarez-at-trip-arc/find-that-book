/** Picture of a mostly empty bookshelf, shown under the search box when a search finds no books. */
export function EmptyShelf() {
  return (
    <div className="empty-shelf">
      <svg viewBox="0 0 320 220" role="img" aria-label="An empty bookshelf">
        <rect className="es-frame" x="30" y="14" width="260" height="196" rx="6" />
        <rect className="es-back" x="42" y="26" width="236" height="172" />
        <rect className="es-board" x="30" y="84" width="260" height="10" />
        <rect className="es-board" x="30" y="150" width="260" height="10" />
        <rect className="es-board" x="30" y="202" width="260" height="10" rx="3" />

        <rect className="es-book es-b1" x="56" y="44" width="16" height="40" rx="2" />
        <rect className="es-book es-b2" x="74" y="52" width="14" height="32" rx="2" />
        <rect className="es-book es-b3" x="90" y="40" width="18" height="44" rx="2" />

        <g className="es-lean">
          <rect className="es-book es-b4" x="212" y="104" width="16" height="46" rx="2" />
        </g>
        <rect className="es-end" x="232" y="116" width="8" height="34" rx="2" />

        <rect className="es-book es-b2" x="60" y="178" width="70" height="10" rx="2" />
        <rect className="es-book es-b1" x="64" y="168" width="62" height="10" rx="2" />
        <circle className="es-plant-pot" cx="244" cy="190" r="11" />
        <path className="es-plant" d="M244 180c-8-10-8-18-2-22 2 8 4 14 2 22zm0 0c8-8 12-12 18-12-2 8-8 12-18 12z" />
      </svg>
    </div>
  );
}
