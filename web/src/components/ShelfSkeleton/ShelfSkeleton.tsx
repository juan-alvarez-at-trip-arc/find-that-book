const PLACEHOLDERS = [0, 1, 2, 3];

/** Placeholder books shown on the shelf while a search runs, so results do not shift the layout when they arrive. */
export function ShelfSkeleton() {
  return (
    <ol className="results" aria-hidden="true">
      {PLACEHOLDERS.map((i) => (
        <li key={i} className="book skeleton-book">
          <div className="shelf-slot">
            <div className="cover skeleton-block" />
          </div>
          <div className="skeleton-line wide" />
          <div className="skeleton-line narrow" />
        </li>
      ))}
    </ol>
  );
}
