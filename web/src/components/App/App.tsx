import { useRef, useState, type FormEvent } from "react";
import { searchBooks } from "../../lib/api";
import { promoteExample } from "../../lib/examples";
import { ResultCard } from "../ResultCard/ResultCard";
import { SearchProgress } from "../SearchProgress/SearchProgress";
import { ThemeToggle } from "../ThemeToggle/ThemeToggle";
import type { SearchResponse } from "../../lib/types";
import "./App.css";

const INITIAL_EXAMPLES = [
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
  const [examples, setExamples] = useState(INITIAL_EXAMPLES);
  const latest = useRef(0);

  async function run(text: string) {
    if (!text.trim()) return;
    const id = ++latest.current;
    setLoading(true);
    setError(null);
    setData(null);
    try {
      const response = await searchBooks(text);
      if (id === latest.current) {
        setData(response);
        if (response.results.length > 0) setExamples((current) => promoteExample(current, text));
      }
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
      <header className="top">
        <div>
          <h1>Find That Book</h1>
          <p className="lead">Type whatever you remember: a title, an author, or a messy mix.</p>
        </div>
        <ThemeToggle />
      </header>

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
        {examples.map((example) => (
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
      {loading && <SearchProgress />}
      {data && data.results.length === 0 && <p className="empty">{data.message ?? "No matching books found."}</p>}
      {data && data.results.length > 0 && (
        <ol className="results">
          {data.results.map((book, i) => <ResultCard key={book.workId} book={book} rank={i + 1} />)}
        </ol>
      )}
    </main>
  );
}
