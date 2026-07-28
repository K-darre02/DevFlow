import { apiFetch } from './client'
import type { SearchResults } from './types'

// A small, fixed pageSize — this powers a compact dropdown, not a listing
// page, so there's no paging UI to wire up to a larger value.
export function search(query: string) {
  return apiFetch<SearchResults>(`/search?q=${encodeURIComponent(query)}&pageSize=5`)
}
