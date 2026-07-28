import { apiFetch, ApiError } from './client'
import { useAuthStore } from '../auth/authStore'
import type { Attachment } from './types'

export function getAttachments(taskId: string) {
  return apiFetch<Attachment[]>(`/tasks/${taskId}/attachments`)
}

export function uploadAttachment(taskId: string, file: File) {
  const formData = new FormData()
  formData.append('file', file)
  return apiFetch<Attachment>(`/tasks/${taskId}/attachments`, { method: 'POST', body: formData })
}

export function deleteAttachment(taskId: string, attachmentId: string) {
  return apiFetch<void>(`/tasks/${taskId}/attachments/${attachmentId}`, { method: 'DELETE' })
}

// Not apiFetch: the response here is the file's own bytes, not JSON — the
// server-side endpoint itself is a 302 to a short-lived signed URL
// (TaskAttachmentsController.Download), which fetch() follows
// transparently, so this one authenticated call is really two requests
// under the hood and this function only ever sees the final one.
export async function downloadAttachment(taskId: string, attachmentId: string, fileName: string): Promise<void> {
  const token = useAuthStore.getState().accessToken
  const response = await fetch(`/api/tasks/${taskId}/attachments/${attachmentId}/download`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })

  if (response.status === 401) {
    useAuthStore.getState().logout()
  }

  if (!response.ok) {
    throw new ApiError(response.status, `Could not download the attachment (${response.status}).`)
  }

  const blob = await response.blob()
  const objectUrl = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = objectUrl
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(objectUrl)
}
