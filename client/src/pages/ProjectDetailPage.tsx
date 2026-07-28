import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { getProject } from '../api/projects'
import { KanbanBoard } from '../components/kanban/KanbanBoard'
import { Badge } from '../components/ui/Badge'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Spinner } from '../components/ui/Spinner'

export function ProjectDetailPage() {
  const { id } = useParams<{ id: string }>()
  const projectQuery = useQuery({
    queryKey: ['project', id],
    queryFn: () => getProject(id as string),
    enabled: Boolean(id),
  })

  if (!id) {
    return <ErrorBanner messages={['No project specified.']} />
  }

  if (projectQuery.isLoading) {
    return <Spinner label="Loading project…" />
  }

  if (projectQuery.isError || !projectQuery.data) {
    const isNotFound = projectQuery.error instanceof ApiError && projectQuery.error.status === 404
    return (
      <div className="flex flex-col gap-3">
        <ErrorBanner
          messages={
            isNotFound
              ? ['This project does not exist, or you do not have access to it.']
              : projectQuery.error instanceof ApiError
                ? projectQuery.error.messages
                : ['Could not load this project.']
          }
        />
        <Link to="/" className="text-sm font-medium text-slate-900 underline">
          Back to projects
        </Link>
      </div>
    )
  }

  const project = projectQuery.data

  return (
    <div className="flex flex-col gap-6">
      <div>
        <Link to="/" className="text-sm text-slate-500 hover:text-slate-700">
          ← Projects
        </Link>
        <div className="mt-1 flex items-center gap-3">
          <h1 className="text-2xl font-semibold text-slate-900">{project.name}</h1>
          {project.isArchived && <Badge color="gray">Archived</Badge>}
        </div>
        <p className="mt-1 text-sm text-slate-500">Created {new Date(project.createdAt).toLocaleDateString()}</p>
      </div>

      <KanbanBoard projectId={project.id} />
    </div>
  )
}
