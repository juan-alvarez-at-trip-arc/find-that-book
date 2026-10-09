import { useState } from "react";
import { badgeFor } from "../../lib/badge";
import type { BookResult } from "../../lib/types";

function Cover({ book, className, priority = false }: { book: BookResult; className: string; priority?: boolean }) {
  const [failed, setFailed] = useState(false);
  return (
    <a className="cover-link" href={book.url} target="_blank" rel="noreferrer" aria-label={`${shownTitle(book)}, view on Open Library`}>
      {book.coverUrl && !failed ? (
        <img className={className} src={book.coverUrl} alt={`Cover of ${shownTitle(book)}`} loading={priority ? "eager" : "lazy"} fetchPriority={priority ? "high" : "auto"} decoding="async" onError={() => setFailed(true)} />
      ) : (
        <div className={`${className} placeholder`} aria-hidden="true" data-title={shownTitle(book)} />
      )}
    </a>
  );
}

function Byline({ book }: { book: BookResult }) {
  return (
    <p className="by">
      {book.author || "Unknown author"}
      {book.first_publish_year ? ` · ${book.first_publish_year}` : ""}
    </p>
  );
}

function shownTitle(book: BookResult): string {
  return book.matchedTitle ?? book.title;
}

function Catalogued({ book }: { book: BookResult }) {
  return book.matchedTitle ? <p className="matched">Catalogued as “{book.title}”</p> : null;
}

/** Shows the top match large with its reason visible, then the other matches as compact cards with the reason collapsed. */
export function FeaturedResults({ results }: { results: BookResult[] }) {
  const [top, ...rest] = results;
  const topBadge = badgeFor(top, 1);

  return (
    <div className="featured-wrap">
      <article className="featured">
        <Cover book={top} className="cover featured-cover" priority />
        <div className="featured-body">
          <span className={`pill ${topBadge.kind}`}>{topBadge.label}</span>
          <h3 className="featured-title">{shownTitle(top)}</h3>
          <Catalogued book={top} />
          <Byline book={top} />
          <p className="featured-why">{top.explanation}</p>
          <a className="featured-link" href={top.url} target="_blank" rel="noreferrer">View on Open Library</a>
        </div>
      </article>
      {rest.length > 0 && (
        <>
          <h3 className="more">Other possible matches</h3>
          <ol className="rest">
            {rest.map((book, i) => {
              const badge = badgeFor(book, i + 2);
              return (
                <li key={book.workId} className="mini">
                  <Cover book={book} className="cover mini-cover" />
                  <div>
                    <span className={`pill ${badge.kind}`}>{badge.label}</span>
                    <h4 className="mini-title">{shownTitle(book)}</h4>
                    <Catalogued book={book} />
                    <Byline book={book} />
                    <details>
                      <summary>Why this book?</summary>
                      <p className="mini-why">{book.explanation}</p>
                    </details>
                  </div>
                </li>
              );
            })}
          </ol>
        </>
      )}
    </div>
  );
}
