import { useRef, useState, type ChangeEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { deleteAttachment, downloadAttachment, getAttachments, uploadAttachment } from '../../api/attachments'
import { ApiError } from '../../api/client'
import type { Attachment } from '../../api/types'
import { Button } from '../ui/Button'
import { ErrorBanner } from '../ui/ErrorBanner'
import { Spinner } from '../ui/Spinner'

interface TaskAttachmentsProps {
  taskId: string
}

// Mirrors UploadAttachmentInputValidator.MaxSizeBytes in DevFlow.Application
// — catches the common case client-side before round-tripping to the
// server, same pattern as RegisterPage's password-length pre-check.
const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function TaskAttachments({ taskId }: TaskAttachmentsProps) {
  const queryClient = useQueryClient()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const attachmentsQuery = useQuery({ queryKey: ['attachments', taskId], queryFn: () => getAttachments(taskId) })

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment(taskId, file),
    onSuccess: () => {
      setError(null)
      void queryClient.invalidateQueries({ queryKey: ['attachments', taskId] })
    },
    onError: (err: unknown) => {
      setError(err instanceof ApiError ? err.messages.join(' ') : 'Could not upload the file.')
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (attachmentId: string) => deleteAttachment(taskId, attachmentId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['attachments', taskId] }),
    onError: () => setError('Could not delete the attachment.'),
  })

  function handleFileSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = '' // allow re-selecting the same file later

    if (!file) return

    if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
      setError(`'${file.name}' is larger than the 10 MB limit.`)
      return
    }

    setError(null)
    uploadMutation.mutate(file)
  }

  async function handleDownload(attachment: Attachment) {
    setDownloadingId(attachment.id)
    setError(null)
    try {
      await downloadAttachment(taskId, attachment.id, attachment.fileName)
    } catch {
      setError('Could not download the attachment.')
    } finally {
      setDownloadingId(null)
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-slate-700">Attachments</span>
        <Button
          type="button"
          variant="secondary"
          isLoading={uploadMutation.isPending}
          onClick={() => fileInputRef.current?.click()}
        >
          Upload file
        </Button>
        <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileSelected} />
      </div>

      {error && <ErrorBanner messages={[error]} />}

      {attachmentsQuery.isLoading && <Spinner label="Loading attachments…" />}

      {attachmentsQuery.data?.length === 0 && <p className="text-xs text-slate-500">No attachments yet.</p>}

      {attachmentsQuery.data && attachmentsQuery.data.length > 0 && (
        <ul className="flex flex-col gap-1.5">
          {attachmentsQuery.data.map((attachment) => (
            <li
              key={attachment.id}
              className="flex items-center justify-between gap-2 rounded-md border border-slate-200 px-2.5 py-1.5"
            >
              <div className="min-w-0">
                <p className="truncate text-sm text-slate-900">{attachment.fileName}</p>
                <p className="text-xs text-slate-500">
                  {formatFileSize(attachment.size)} · {attachment.uploadedByEmail ?? 'Unknown'}
                </p>
              </div>
              <div className="flex flex-shrink-0 items-center gap-3">
                <Button
                  type="button"
                  variant="link"
                  className="text-xs"
                  onClick={() => void handleDownload(attachment)}
                  disabled={downloadingId === attachment.id}
                >
                  {downloadingId === attachment.id ? 'Downloading…' : 'Download'}
                </Button>
                <Button
                  type="button"
                  variant="link-danger"
                  className="text-xs"
                  onClick={() => deleteMutation.mutate(attachment.id)}
                  disabled={deleteMutation.isPending && deleteMutation.variables === attachment.id}
                >
                  Delete
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
