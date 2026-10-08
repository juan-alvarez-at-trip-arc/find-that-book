import type { BookResult } from "../../lib/types";

export function ResultCard({ book, rank }: { book: BookResult; rank: number }) {
  return (
    <li className="card">
      {book.coverUrl ? (
        <img className="cover" src={book.coverUrl} alt={`Cover of ${book.title}`} loading="lazy" />
      ) : (
        <div className="cover placeholder" aria-hidden="true">No cover</div>
      )}
      <div className="details">
        <span className="rank">#{rank}</span>
        <h2>{book.title}</h2>
        <p className="meta">
          {book.author || "Unknown author"}
          {book.first_publish_year ? ` · first published ${book.first_publish_year}` : ""}
        </p>
        <p className="why">{book.explanation}</p>
        <a href={book.url} target="_blank" rel="noreferrer">View on Open Library</a>
      </div>
    </li>
  );
}
