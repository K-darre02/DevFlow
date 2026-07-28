import type { ActivityEntityType, ActivityType } from '../api/types'

export const ACTIVITY_TYPE_OPTIONS: ActivityType[] = [
  'ProjectCreated',
  'ProjectArchived',
  'TaskCreated',
  'TaskUpdated',
  'TaskDeleted',
  'TaskMoved',
  'TaskAssigned',
  'TaskCompleted',
  'MemberInvited',
  'MemberJoined',
  'RoleChanged',
  'MemberRemoved',
]

export const ENTITY_TYPE_OPTIONS: ActivityEntityType[] = ['Project', 'Task', 'Invitation', 'TeamMember']

// Loosely grouped by "flavor" of action rather than one color per exact
// type — keeps the timeline scannable rather than turning into 12 competing colors.
const CREATED_OR_JOINED: ActivityType[] = ['ProjectCreated', 'TaskCreated', 'MemberJoined']
const REMOVED_OR_DELETED: ActivityType[] = ['ProjectArchived', 'TaskDeleted', 'MemberRemoved']
const COMPLETED: ActivityType[] = ['TaskCompleted']

export function activityDotColor(activityType: ActivityType): string {
  if (CREATED_OR_JOINED.includes(activityType)) return 'bg-green-500'
  if (REMOVED_OR_DELETED.includes(activityType)) return 'bg-red-400'
  if (COMPLETED.includes(activityType)) return 'bg-blue-500'
  return 'bg-slate-400'
}
