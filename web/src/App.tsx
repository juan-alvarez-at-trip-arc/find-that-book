import { useRef, useState, type FormEvent } from "react";
import { searchBooks } from "./api";
import { ResultCard } from "./ResultCard";
import type { SearchResponse } from "./types";
import "./App.css";

const EXAMPLES = [
  "tolkien hobbit illustrated deluxe 1937",
  "mark huckleberry",
  "austen bennet",
  "dickens",
  "twilight meyer",
];

export default function App() {
  const [query, setQuery] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<SearchResponse | null>(null);
  const latest = useRef(0);

  async function run(text: string) {
    if (!text.trim()) return;
    const id = ++latest.current;
    setLoading(true);
    setError(null);
    try {
      const response = await searchBooks(text);
      if (id === latest.current) setData(response);
    } catch (e) {
      if (id !== latest.current) return;
      setData(null);
      setError(e instanceof Error && !(e instanceof TypeError) ? e.message : "Something went wrong. Please try again.");
    } finally {
      if (id === latest.current) setLoading(false);
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void run(query);
  }

  return (
    <main className="page">
      <h1>Find That Book</h1>
      <p className="lead">Type whatever you remember: a title, an author, or a messy mix.</p>

      <form onSubmit={onSubmit} className="search">
        <input
          type="search"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="e.g. tolkien hobbit illustrated deluxe 1937"
          aria-label="Search for a book"
          maxLength={200}
        />
        <button type="submit" disabled={loading}>{loading ? "Searching…" : "Search"}</button>
      </form>

      <div className="examples">
        {EXAMPLES.map((example) => (
          <button
            key={example}
            type="button"
            className="chip"
            onClick={() => {
              setQuery(example);
              void run(example);
            }}
          >
            {example}
          </button>
        ))}
      </div>

      {error && <div role="alert" className="banner error">{error}</div>}
      {data && !data.aiUsed && (
        <div className="banner notice">AI is unavailable, so results use basic parsing and may be less precise.</div>
      )}
      {loading && <div className="skeleton" aria-hidden="true" />}
      {data && data.results.length === 0 && <p className="empty">{data.message ?? "No matching books found."}</p>}
      {data && data.results.length > 0 && (
        <ol className="results">
          {data.results.map((book, i) => <ResultCard key={book.workId} book={book} rank={i + 1} />)}
        </ol>
      )}
    </main>
  );
}
