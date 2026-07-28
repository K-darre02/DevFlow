import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { createProject, getProjects } from '../api/projects'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Input } from '../components/ui/Input'

export function ProjectsPage() {
  const queryClient = useQueryClient()
  const projectsQuery = useQuery({ queryKey: ['projects'], queryFn: () => getProjects() })

  const [name, setName] = useState('')
  const [createErrors, setCreateErrors] = useState<string[]>([])

  const createMutation = useMutation({
    mutationFn: createProject,
    onSuccess: () => {
      setName('')
      setCreateErrors([])
      void queryClient.invalidateQueries({ queryKey: ['projects'] })
    },
    onError: (error: unknown) => {
      setCreateErrors(error instanceof ApiError ? error.messages : ['Could not create the project.'])
    },
  })

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) {
      return
    }
    createMutation.mutate({ name: trimmed })
  }

  return (
    <div className="flex flex-col gap-8">
      <Card>
        <h2 className="mb-4 text-lg font-semibold text-slate-900">New project</h2>
        <form onSubmit={handleSubmit} className="flex items-end gap-3">
          <div className="flex-1">
            <Input
              label="Project name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              required
            />
          </div>
          <Button type="submit" isLoading={createMutation.isPending}>
            Create project
          </Button>
        </form>
        <div className="mt-3">
          <ErrorBanner messages={createErrors} />
        </div>
      </Card>

      <Card>
        <h2 className="mb-4 text-lg font-semibold text-slate-900">Projects</h2>

        {projectsQuery.isLoading && <p className="text-sm text-slate-500">Loading projects…</p>}

        {projectsQuery.isError && (
          <ErrorBanner
            messages={
              projectsQuery.error instanceof ApiError
                ? projectsQuery.error.messages
                : ['Could not load projects.']
            }
          />
        )}

        {projectsQuery.data?.length === 0 && (
          <p className="text-sm text-slate-500">No projects yet — create your first one above.</p>
        )}

        {projectsQuery.data && projectsQuery.data.length > 0 && (
          <ul className="divide-y divide-slate-200">
            {projectsQuery.data.map((project) => (
              <li key={project.id} className="flex items-center justify-between py-3">
                <span className="font-medium text-slate-900">{project.name}</span>
                <span className="text-xs text-slate-500">
                  Created {new Date(project.createdAt).toLocaleDateString()}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
