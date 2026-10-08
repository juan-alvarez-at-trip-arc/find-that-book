export interface BookResult {
  title: string;
  author: string;
  authors: string[];
  first_publish_year: number | null;
  workId: string;
  url: string;
  coverUrl: string | null;
  explanation: string;
  matchTier: number;
  exactTitle: boolean;
}

export interface SearchResponse {
  aiUsed: boolean;
  message: string | null;
  results: BookResult[];
}
