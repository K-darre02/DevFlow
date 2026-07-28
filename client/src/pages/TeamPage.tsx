import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { changeRole, getTeam, inviteMember, removeMember } from '../api/team'
import { ApiError } from '../api/client'
import type { Invitation, TenantRole } from '../api/types'
import { useAuthStore } from '../auth/authStore'
import { Badge } from '../components/ui/Badge'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Input } from '../components/ui/Input'
import { Select } from '../components/ui/Select'
import { Spinner } from '../components/ui/Spinner'

const ROLE_BADGE_COLOR: Record<TenantRole, 'gray' | 'blue' | 'green'> = {
  Owner: 'green',
  Admin: 'blue',
  Member: 'gray',
}

// Roles an inviter may grant — matches InviteMemberInputValidator, which
// rejects Owner (a workspace's first Owner only ever comes from Register).
const INVITABLE_ROLES: TenantRole[] = ['Member', 'Admin']

// ChangeRoleAsync itself doesn't restrict the target role, so an Owner can
// promote someone straight to Owner (multiple Owners are allowed).
const ASSIGNABLE_ROLES: TenantRole[] = ['Member', 'Admin', 'Owner']

export function TeamPage() {
  const queryClient = useQueryClient()
  const currentUser = useAuthStore((state) => state.user)
  const canManageTeam = currentUser?.role === 'Owner' || currentUser?.role === 'Admin'
  const canChangeRoles = currentUser?.role === 'Owner'

  const teamQuery = useQuery({ queryKey: ['team'], queryFn: getTeam })

  const [inviteEmail, setInviteEmail] = useState('')
  const [inviteRole, setInviteRole] = useState<TenantRole>('Member')
  const [inviteErrors, setInviteErrors] = useState<string[]>([])
  const [lastInvitation, setLastInvitation] = useState<Invitation | null>(null)

  const inviteMutation = useMutation({
    mutationFn: inviteMember,
    onSuccess: (invitation) => {
      setInviteEmail('')
      setInviteRole('Member')
      setInviteErrors([])
      setLastInvitation(invitation)
      void queryClient.invalidateQueries({ queryKey: ['team'] })
    },
    onError: (error: unknown) => {
      setInviteErrors(error instanceof ApiError ? error.messages : ['Could not send the invitation.'])
    },
  })

  const changeRoleMutation = useMutation({
    mutationFn: ({ memberId, role }: { memberId: string; role: TenantRole }) => changeRole(memberId, { role }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['team'] }),
  })

  const removeMutation = useMutation({
    mutationFn: removeMember,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['team'] }),
  })

  function handleInvite(event: FormEvent) {
    event.preventDefault()
    const trimmed = inviteEmail.trim()
    if (!trimmed) {
      return
    }
    inviteMutation.mutate({ email: trimmed, role: inviteRole })
  }

  const acceptLink = lastInvitation ? `${window.location.origin}/accept-invitation?token=${lastInvitation.token}` : null

  async function copyAcceptLink() {
    if (acceptLink) {
      await navigator.clipboard.writeText(acceptLink)
    }
  }

  return (
    <div className="flex flex-col gap-8">
      {canManageTeam && (
        <Card>
          <h2 className="mb-4 text-lg font-semibold text-slate-900">Invite a member</h2>
          <form onSubmit={handleInvite} className="flex items-end gap-3">
            <div className="flex-1">
              <Input
                label="Email"
                type="email"
                value={inviteEmail}
                onChange={(event) => setInviteEmail(event.target.value)}
                required
              />
            </div>
            <div className="w-40">
              <Select
                label="Role"
                value={inviteRole}
                onChange={(event) => setInviteRole(event.target.value as TenantRole)}
              >
                {INVITABLE_ROLES.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </Select>
            </div>
            <Button type="submit" isLoading={inviteMutation.isPending}>
              Send invite
            </Button>
          </form>
          <div className="mt-3">
            <ErrorBanner messages={inviteErrors} />
          </div>

          {acceptLink && (
            <div className="mt-4 flex items-end gap-3 rounded-md border border-green-200 bg-green-50 p-3">
              <div className="flex-1">
                <Input
                  label={`Invite link for ${lastInvitation!.email} — share this with them (there's no email delivery yet)`}
                  readOnly
                  value={acceptLink}
                  onFocus={(event) => event.target.select()}
                />
              </div>
              <Button type="button" variant="secondary" onClick={() => void copyAcceptLink()}>
                Copy
              </Button>
            </div>
          )}
        </Card>
      )}

      <Card>
        <h2 className="mb-4 text-lg font-semibold text-slate-900">Team members</h2>

        {teamQuery.isLoading && <Spinner label="Loading team…" />}

        {teamQuery.isError && (
          <ErrorBanner
            messages={teamQuery.error instanceof ApiError ? teamQuery.error.messages : ['Could not load the team.']}
          />
        )}

        {teamQuery.data?.length === 0 && <p className="text-sm text-slate-500">No team members yet.</p>}

        {teamQuery.data && teamQuery.data.length > 0 && (
          <ul className="divide-y divide-slate-200">
            {teamQuery.data.map((member) => (
              <li key={member.id} className="flex items-center justify-between gap-4 py-3">
                <div className="flex items-center gap-3">
                  <span className="font-medium text-slate-900">{member.email}</span>
                  <Badge color={ROLE_BADGE_COLOR[member.role]}>{member.role}</Badge>
                </div>

                <div className="flex items-center gap-3">
                  <span className="text-xs text-slate-500">
                    Joined {new Date(member.joinedAt).toLocaleDateString()}
                  </span>

                  {canChangeRoles && (
                    <div className="w-28">
                      <Select
                        label={`Role for ${member.email}`}
                        hideLabel
                        value={member.role}
                        disabled={changeRoleMutation.isPending}
                        onChange={(event) =>
                          changeRoleMutation.mutate({ memberId: member.id, role: event.target.value as TenantRole })
                        }
                      >
                        {ASSIGNABLE_ROLES.map((role) => (
                          <option key={role} value={role}>
                            {role}
                          </option>
                        ))}
                      </Select>
                    </div>
                  )}

                  {canManageTeam && (
                    <Button
                      type="button"
                      variant="danger"
                      isLoading={removeMutation.isPending && removeMutation.variables === member.id}
                      onClick={() => removeMutation.mutate(member.id)}
                    >
                      Remove
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}

        {changeRoleMutation.isError && (
          <div className="mt-3">
            <ErrorBanner
              messages={
                changeRoleMutation.error instanceof ApiError
                  ? changeRoleMutation.error.messages
                  : ['Could not change that member’s role.']
              }
            />
          </div>
        )}

        {removeMutation.isError && (
          <div className="mt-3">
            <ErrorBanner
              messages={
                removeMutation.error instanceof ApiError ? removeMutation.error.messages : ['Could not remove that member.']
              }
            />
          </div>
        )}
      </Card>
    </div>
  )
}
