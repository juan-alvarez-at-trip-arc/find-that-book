/** Placeholder shaped like the featured match and two other matches, shown while a search runs so results do not shift the layout when they arrive. */
export function ResultsSkeleton() {
  return (
    <div className="featured-wrap skeleton" aria-hidden="true">
      <div className="featured">
        <div className="cover featured-cover skeleton-block" />
        <div className="featured-body">
          <div className="skeleton-line pill-line" />
          <div className="skeleton-line wide" />
          <div className="skeleton-line narrow" />
          <div className="skeleton-line wide" />
        </div>
      </div>
      <div className="rest">
        {[0, 1].map((i) => (
          <div key={i} className="mini">
            <div className="cover mini-cover skeleton-block" />
            <div>
              <div className="skeleton-line wide" />
              <div className="skeleton-line narrow" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
