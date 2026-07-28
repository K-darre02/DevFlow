import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { search } from '../../api/search'
import type { ProjectSearchResult, TaskSearchResult, UserSearchResult } from '../../api/types'
import { Spinner } from '../ui/Spinner'
import { STATUS_COLUMNS } from '../../lib/taskDisplay'

const DEBOUNCE_MS = 300

function statusLabel(status: TaskSearchResult['status']): string {
  return STATUS_COLUMNS.find((column) => column.status === status)?.label ?? status
}

export function GlobalSearch() {
  const [query, setQuery] = useState('')
  const [debouncedQuery, setDebouncedQuery] = useState('')
  const [isOpen, setIsOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    const timeout = setTimeout(() => setDebouncedQuery(query.trim()), DEBOUNCE_MS)
    return () => clearTimeout(timeout)
  }, [query])

  const searchQuery = useQuery({
    queryKey: ['search', debouncedQuery],
    queryFn: () => search(debouncedQuery),
    enabled: debouncedQuery.length > 0,
  })

  // Global Cmd/Ctrl+K shortcut — active regardless of whether the dropdown
  // is currently open, unlike the outside-click/Escape handling below.
  useEffect(() => {
    function handleShortcut(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setIsOpen(true)
        inputRef.current?.focus()
      }
    }

    document.addEventListener('keydown', handleShortcut)
    return () => document.removeEventListener('keydown', handleShortcut)
  }, [])

  useEffect(() => {
    if (!isOpen) return

    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false)
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false)
        inputRef.current?.blur()
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [isOpen])

  function goTo(path: string) {
    navigate(path)
    setIsOpen(false)
    setQuery('')
  }

  function goToProject(project: ProjectSearchResult) {
    goTo(`/projects/${project.id}`)
  }

  function goToTask(task: TaskSearchResult) {
    goTo(`/projects/${task.projectId}`)
  }

  function goToUser(_user: UserSearchResult) {
    goTo('/team')
  }

  const results = searchQuery.data
  const hasAnyResults =
    !!results && (results.projects.items.length > 0 || results.tasks.items.length > 0 || results.users.items.length > 0)
  const showDropdown = isOpen && debouncedQuery.length > 0

  return (
    <div className="relative w-64" ref={containerRef}>
      <div className="relative">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="7" />
          <path strokeLinecap="round" d="m20 20-3.5-3.5" />
        </svg>
        <input
          ref={inputRef}
          type="text"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          onFocus={() => setIsOpen(true)}
          placeholder="Search…"
          aria-label="Global search"
          className="w-full rounded-md border border-slate-200 bg-slate-50 py-1.5 pl-8 pr-10 text-sm text-slate-900 placeholder:text-slate-400 focus:border-slate-400 focus:bg-white focus:outline-none"
        />
        {!query && (
          <span className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 rounded border border-slate-200 px-1 text-[10px] font-medium text-slate-400">
            ⌘K
          </span>
        )}
      </div>

      {showDropdown && (
        <div className="absolute left-0 right-0 z-40 mt-2 max-h-96 overflow-y-auto rounded-lg border border-slate-200 bg-white shadow-lg">
          {searchQuery.isLoading && <Spinner label="Searching…" />}

          {searchQuery.isError && (
            <p className="px-4 py-6 text-center text-sm text-red-600">Search failed. Try again.</p>
          )}

          {results && !hasAnyResults && (
            <p className="px-4 py-6 text-center text-sm text-slate-500">No results for &ldquo;{debouncedQuery}&rdquo;.</p>
          )}

          {results && results.projects.items.length > 0 && (
            <div className="border-b border-slate-100 py-2">
              <p className="px-4 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Projects</p>
              {results.projects.items.map((project) => (
                <button
                  key={project.id}
                  type="button"
                  onClick={() => goToProject(project)}
                  className="block w-full px-4 py-2 text-left text-sm text-slate-900 hover:bg-slate-50"
                >
                  {project.name}
                  {project.isArchived && <span className="ml-2 text-xs text-slate-400">Archived</span>}
                </button>
              ))}
            </div>
          )}

          {results && results.tasks.items.length > 0 && (
            <div className="border-b border-slate-100 py-2">
              <p className="px-4 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Tasks</p>
              {results.tasks.items.map((task) => (
                <button
                  key={task.id}
                  type="button"
                  onClick={() => goToTask(task)}
                  className="block w-full px-4 py-2 text-left hover:bg-slate-50"
                >
                  <span className="block truncate text-sm text-slate-900">{task.title}</span>
                  <span className="text-xs text-slate-400">
                    {task.projectName} · {statusLabel(task.status)}
                  </span>
                </button>
              ))}
            </div>
          )}

          {results && results.users.items.length > 0 && (
            <div className="py-2">
              <p className="px-4 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">People</p>
              {results.users.items.map((user) => (
                <button
                  key={user.userId}
                  type="button"
                  onClick={() => goToUser(user)}
                  className="block w-full px-4 py-2 text-left hover:bg-slate-50"
                >
                  <span className="block truncate text-sm text-slate-900">{user.email}</span>
                  <span className="text-xs text-slate-400">{user.role}</span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
