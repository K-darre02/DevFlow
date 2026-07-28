import { apiFetch } from './client'
import type { AcceptInvitationRequest, AuthResponse, ChangeRoleRequest, Invitation, InviteMemberRequest, TeamMember } from './types'

export function getTeam() {
  return apiFetch<TeamMember[]>('/team')
}

export function inviteMember(request: InviteMemberRequest) {
  return apiFetch<Invitation>('/team/invitations', { method: 'POST', body: request })
}

// Anonymous: accepting an invitation is how a brand-new user gets their
// first session, so there's no bearer token to attach yet — mirrors
// api/auth.ts's login/register.
export function acceptInvitation(request: AcceptInvitationRequest) {
  return apiFetch<AuthResponse>('/team/invitations/accept', { method: 'POST', body: request, auth: false })
}

export function changeRole(memberId: string, request: ChangeRoleRequest) {
  return apiFetch<TeamMember>(`/team/${memberId}/role`, { method: 'PATCH', body: request })
}

export function removeMember(memberId: string) {
  return apiFetch<void>(`/team/${memberId}`, { method: 'DELETE' })
}
