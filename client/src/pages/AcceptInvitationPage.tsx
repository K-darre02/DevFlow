import { useState, type FormEvent } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { acceptInvitation } from '../api/team'
import { ApiError } from '../api/client'
import { useAuthStore } from '../auth/authStore'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Input } from '../components/ui/Input'

export function AcceptInvitationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const setAuth = useAuthStore((state) => state.login)

  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])

    if (password.length < 8) {
      setErrors(['Password must be at least 8 characters.'])
      return
    }

    setIsSubmitting(true)
    try {
      const auth = await acceptInvitation({ token, password })
      setAuth(auth)
      navigate('/', { replace: true })
    } catch (error) {
      setErrors(error instanceof ApiError ? error.messages : ['Something went wrong. Please try again.'])
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4">
      <Card className="w-full max-w-sm">
        <h1 className="mb-2 text-xl font-semibold text-slate-900">Join your team on DevFlow</h1>
        <p className="mb-6 text-sm text-slate-600">
          Set a password to accept your invitation. If you already have a DevFlow account with this email, sign in
          with your existing password to link it to this workspace.
        </p>

        {!token ? (
          <ErrorBanner messages={['This invitation link is missing its token — ask for a new invite.']} />
        ) : (
          <form onSubmit={handleSubmit} className="flex flex-col gap-4">
            <Input
              label="Password"
              type="password"
              autoComplete="new-password"
              required
              minLength={8}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <ErrorBanner messages={errors} />
            <Button type="submit" isLoading={isSubmitting}>
              Accept invitation
            </Button>
          </form>
        )}
      </Card>
    </div>
  )
}
