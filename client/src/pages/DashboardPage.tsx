import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getDashboard } from '../api/dashboard'
import { ApiError } from '../api/client'
import type { TaskPriority } from '../api/types'
import { BarChart } from '../components/dashboard/BarChart'
import { StatCard } from '../components/dashboard/StatCard'
import { Badge } from '../components/ui/Badge'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Spinner } from '../components/ui/Spinner'
import { activityDotColor } from '../lib/activityDisplay'
import { PRIORITY_BADGE_COLOR, PRIORITY_BAR_COLOR, STATUS_BAR_COLOR, STATUS_COLUMNS } from '../lib/taskDisplay'

const PRIORITY_ORDER: TaskPriority[] = ['Urgent', 'High', 'Medium', 'Low']

function formatOverdueBy(dueDate: string): string {
  const due = new Date(`${dueDate}T00:00:00`)
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  const days = Math.round((today.getTime() - due.getTime()) / (1000 * 60 * 60 * 24))
  if (days <= 0) return 'Due today'
  return days === 1 ? '1 day overdue' : `${days} days overdue`
}

export function DashboardPage() {
  const dashboardQuery = useQuery({ queryKey: ['dashboard'], queryFn: getDashboard })

  if (dashboardQuery.isLoading) {
    return <Spinner label="Loading dashboard…" />
  }

  if (dashboardQuery.isError || !dashboardQuery.data) {
    return (
      <ErrorBanner
        messages={
          dashboardQuery.error instanceof ApiError ? dashboardQuery.error.messages : ['Could not load the dashboard.']
        }
      />
    )
  }

  const dashboard = dashboardQuery.data
  const completionRate =
    dashboard.taskCount === 0 ? 0 : Math.round((dashboard.completedTaskCount / dashboard.taskCount) * 100)

  return (
    <div className="flex flex-col gap-6">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="Active projects" value={dashboard.projectCount} accentClass="bg-slate-900" />
        <StatCard label="Total tasks" value={dashboard.taskCount} accentClass="bg-sky-500" />
        <StatCard label="Completed tasks" value={dashboard.completedTaskCount} accentClass="bg-green-500" />
        <StatCard label="Overdue tasks" value={dashboard.overdueTaskCount} accentClass="bg-red-500" />
      </div>

      <Card>
        <div className="mb-2 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-slate-900">Completion progress</h2>
          <span className="text-sm font-medium text-slate-600">{completionRate}%</span>
        </div>
        <div className="h-2.5 overflow-hidden rounded-full bg-slate-100">
          <div
            className="h-2.5 rounded-full bg-green-500 transition-all"
            style={{ width: `${completionRate}%` }}
          />
        </div>
        <p className="mt-2 text-xs text-slate-500">
          {dashboard.completedTaskCount} of {dashboard.taskCount} tasks completed
        </p>
      </Card>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card>
          <h2 className="mb-4 text-lg font-semibold text-slate-900">Tasks by status</h2>
          <BarChart
            rows={STATUS_COLUMNS.map(({ status, label }) => ({
              label,
              count: dashboard.tasksByStatus[status] ?? 0,
              colorClass: STATUS_BAR_COLOR[status],
            }))}
          />
        </Card>

        <Card>
          <h2 className="mb-4 text-lg font-semibold text-slate-900">Tasks by priority</h2>
          <BarChart
            rows={PRIORITY_ORDER.map((priority) => ({
              label: priority,
              count: dashboard.tasksByPriority[priority] ?? 0,
              colorClass: PRIORITY_BAR_COLOR[priority],
            }))}
          />
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card>
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-lg font-semibold text-slate-900">Recent activity</h2>
            <Link to="/activity" className="text-xs font-medium text-slate-600 hover:text-slate-900">
              View all
            </Link>
          </div>

          {dashboard.recentActivity.length === 0 && <p className="text-sm text-slate-500">No activity yet.</p>}

          <ul className="flex flex-col gap-3">
            {dashboard.recentActivity.map((activity) => (
              <li key={activity.id} className="flex gap-3">
                <span
                  className={`mt-1.5 h-2 w-2 flex-shrink-0 rounded-full ${activityDotColor(activity.activityType)}`}
                  aria-hidden="true"
                />
                <div>
                  <p className="text-sm text-slate-900">{activity.description}</p>
                  <p className="text-xs text-slate-400">{new Date(activity.createdAt).toLocaleString()}</p>
                </div>
              </li>
            ))}
          </ul>
        </Card>

        <Card>
          <h2 className="mb-4 text-lg font-semibold text-slate-900">Overdue tasks</h2>

          {dashboard.overdueTasks.length === 0 && (
            <p className="text-sm text-slate-500">Nothing overdue — nice work.</p>
          )}

          <ul className="flex flex-col divide-y divide-slate-100">
            {dashboard.overdueTasks.map((task) => (
              <li key={task.id} className="flex items-center justify-between gap-2 py-2.5 first:pt-0 last:pb-0">
                <div className="min-w-0">
                  <Link
                    to={`/projects/${task.projectId}`}
                    className="block truncate text-sm font-medium text-slate-900 hover:underline"
                  >
                    {task.title}
                  </Link>
                  <p className="text-xs text-red-600">
                    {formatOverdueBy(task.dueDate)} · {task.projectName}
                  </p>
                </div>
                <Badge color={PRIORITY_BADGE_COLOR[task.priority]}>{task.priority}</Badge>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </div>
  )
}
