import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getActivities } from '../api/activity'
import { ApiError } from '../api/client'
import { getTeam } from '../api/team'
import type { ActivityEntityType, ActivityType } from '../api/types'
import { ACTIVITY_TYPE_OPTIONS, ENTITY_TYPE_OPTIONS, activityDotColor } from '../lib/activityDisplay'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Select } from '../components/ui/Select'
import { Spinner } from '../components/ui/Spinner'

const PAGE_SIZE = 20

export function ActivityPage() {
  const [page, setPage] = useState(1)
  const [entityType, setEntityType] = useState<ActivityEntityType | ''>('')
  const [activityType, setActivityType] = useState<ActivityType | ''>('')
  const [userId, setUserId] = useState('')

  const teamQuery = useQuery({ queryKey: ['team'], queryFn: getTeam })

  const activitiesQuery = useQuery({
    queryKey: ['activities', page, entityType, activityType, userId],
    queryFn: () =>
      getActivities({
        page,
        pageSize: PAGE_SIZE,
        entityType: entityType || undefined,
        activityType: activityType || undefined,
        userId: userId || undefined,
      }),
  })

  function resetToFirstPage<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value)
      setPage(1)
    }
  }

  const totalPages = activitiesQuery.data ? Math.max(1, Math.ceil(activitiesQuery.data.totalCount / PAGE_SIZE)) : 1

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <h2 className="mb-4 text-lg font-semibold text-slate-900">Filters</h2>
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
          <Select
            label="Entity type"
            value={entityType}
            onChange={(event) => resetToFirstPage(setEntityType)(event.target.value as ActivityEntityType | '')}
          >
            <option value="">All</option>
            {ENTITY_TYPE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>

          <Select
            label="Activity type"
            value={activityType}
            onChange={(event) => resetToFirstPage(setActivityType)(event.target.value as ActivityType | '')}
          >
            <option value="">All</option>
            {ACTIVITY_TYPE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </Select>

          <Select
            label="User"
            value={userId}
            disabled={teamQuery.isError}
            error={teamQuery.isError ? 'Could not load members' : undefined}
            onChange={(event) => resetToFirstPage(setUserId)(event.target.value)}
          >
            <option value="">Everyone</option>
            {teamQuery.data?.map((member) => (
              <option key={member.userId} value={member.userId}>
                {member.email}
              </option>
            ))}
          </Select>
        </div>
      </Card>

      <Card>
        <h2 className="mb-4 text-lg font-semibold text-slate-900">Activity</h2>

        {activitiesQuery.isLoading && <Spinner label="Loading activity…" />}

        {activitiesQuery.isError && (
          <ErrorBanner
            messages={
              activitiesQuery.error instanceof ApiError ? activitiesQuery.error.messages : ['Could not load activity.']
            }
          />
        )}

        {activitiesQuery.data?.items.length === 0 && (
          <p className="text-sm text-slate-500">No activity matches these filters.</p>
        )}

        {activitiesQuery.data && activitiesQuery.data.items.length > 0 && (
          <ol className="flex flex-col gap-4">
            {activitiesQuery.data.items.map((activity) => (
              <li key={activity.id} className="flex gap-3">
                <span
                  className={`mt-1.5 h-2.5 w-2.5 flex-shrink-0 rounded-full ${activityDotColor(activity.activityType)}`}
                  aria-hidden="true"
                />
                <div className="flex-1">
                  <p className="text-sm text-slate-900">
                    {activity.description}
                    {activity.entityType === 'Project' && (
                      <>
                        {' '}
                        <Link to={`/projects/${activity.entityId}`} className="font-medium underline hover:text-slate-700">
                          View project
                        </Link>
                      </>
                    )}
                  </p>
                  <p className="mt-0.5 text-xs text-slate-500">
                    {activity.userEmail ?? 'System'} · {new Date(activity.createdAt).toLocaleString()}
                  </p>
                </div>
              </li>
            ))}
          </ol>
        )}

        {activitiesQuery.data && activitiesQuery.data.totalCount > 0 && (
          <div className="mt-6 flex items-center justify-between border-t border-slate-200 pt-4">
            <span className="text-xs text-slate-500">
              Page {activitiesQuery.data.page} of {totalPages} · {activitiesQuery.data.totalCount} total
            </span>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="secondary"
                disabled={page <= 1}
                onClick={() => setPage((current) => Math.max(1, current - 1))}
              >
                Previous
              </Button>
              <Button
                type="button"
                variant="secondary"
                disabled={page >= totalPages}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}
