import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { register } from '../api/auth'
import { ApiError } from '../api/client'
import { useAuthStore } from '../auth/authStore'
import { Button } from '../components/ui/Button'
import { Card } from '../components/ui/Card'
import { ErrorBanner } from '../components/ui/ErrorBanner'
import { Input } from '../components/ui/Input'

export function RegisterPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((state) => state.login)

  const [tenantName, setTenantName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [isSubmitting, setIsSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])

    // Mirrors RegisterRequest's [MinLength(8)] in DevFlow.Api — catches the
    // common case client-side before round-tripping to the server.
    if (password.length < 8) {
      setErrors(['Password must be at least 8 characters.'])
      return
    }

    setIsSubmitting(true)
    try {
      const auth = await register({ tenantName, email, password })
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
        <h1 className="mb-6 text-xl font-semibold text-slate-900">Create your DevFlow workspace</h1>
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <Input
            label="Organization name"
            autoComplete="organization"
            required
            value={tenantName}
            onChange={(event) => setTenantName(event.target.value)}
          />
          <Input
            label="Email"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
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
            Create workspace
          </Button>
        </form>
        <p className="mt-4 text-center text-sm text-slate-600">
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-slate-900 underline">
            Log in
          </Link>
        </p>
      </Card>
    </div>
  )
}
