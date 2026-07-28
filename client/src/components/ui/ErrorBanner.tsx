interface ErrorBannerProps {
  messages: string[]
}

export function ErrorBanner({ messages }: ErrorBannerProps) {
  if (messages.length === 0) {
    return null
  }

  return (
    <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
      {messages.length === 1 ? (
        <p>{messages[0]}</p>
      ) : (
        <ul className="list-inside list-disc space-y-0.5">
          {messages.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
