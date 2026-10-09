import { useId, useState, type KeyboardEvent } from "react";
import { badgeFor } from "../../lib/badge";
import type { BookResult } from "../../lib/types";

export function ResultCard({ book, rank }: { book: BookResult; rank: number }) {
  const [open, setOpen] = useState(false);
  const [dismissed, setDismissed] = useState(false);
  const whyId = useId();
  const badge = badgeFor(book, rank);

  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== "Escape") return;
    setOpen(false);
    setDismissed(true);
  }

  return (
    <li
      className="book"
      data-open={open}
      data-dismissed={dismissed}
      onKeyDown={onKeyDown}
      onMouseLeave={() => setDismissed(false)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setDismissed(false);
      }}
    >
      <div className="shelf-slot">
        <div className="cover-wrap">
          <span className={`badge ${badge.kind}`}>{badge.label}</span>
          <a href={book.url} target="_blank" rel="noreferrer" aria-label={`${book.title}, view on Open Library`}>
            {book.coverUrl ? (
              <img className="cover" src={book.coverUrl} alt={`Cover of ${book.title}`} loading="lazy" />
            ) : (
              <div className="cover placeholder" aria-hidden="true" data-title={book.title} />
            )}
          </a>
        </div>
      </div>
      <h3 className="title">{book.title}</h3>
      <p className="by">
        {book.author || "Unknown author"}
        {book.first_publish_year ? ` · ${book.first_publish_year}` : ""}
      </p>
      <button
        type="button"
        className="why-toggle"
        aria-expanded={open}
        aria-controls={whyId}
        onClick={() => {
          setDismissed(false);
          setOpen((v) => !v);
        }}
      >
        Why this book?
      </button>
      <p id={whyId} className="why">{book.explanation}</p>
    </li>
  );
}
