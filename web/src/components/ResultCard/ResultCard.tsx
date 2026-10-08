import { useId, useState } from "react";
import { badgeFor } from "../../lib/badge";
import type { BookResult } from "../../lib/types";

export function ResultCard({ book, rank }: { book: BookResult; rank: number }) {
  const [open, setOpen] = useState(false);
  const whyId = useId();
  const badge = badgeFor(book, rank);

  return (
    <li className="book" data-open={open}>
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
      <h2 className="title">{book.title}</h2>
      <p className="by">
        {book.author || "Unknown author"}
        {book.first_publish_year ? ` · ${book.first_publish_year}` : ""}
      </p>
      <button type="button" className="why-toggle" aria-expanded={open} aria-controls={whyId} onClick={() => setOpen((v) => !v)}>
        Why this book?
      </button>
      <p id={whyId} className="why">{book.explanation}</p>
    </li>
  );
}
