export type ChatContentPart =
  | { type: 'text'; text: string }
  | { type: 'input_audio'; input_audio: { data: string; format: string } }

export type ChatApiMessage = {
  role: string
  content: string | ChatContentPart[]
}

export type StreamChatBody = {
  model: string
  messages: ChatApiMessage[]
  modalities?: string[]
}

export type SessionStartResponse = {
  sessionId: string
}

export type SessionMessageBody = {
  content: string
  source: 'text' | 'voice'
  model: string
  audioBase64?: string
  audioFormat?: string
}

export type SessionStreamMeta = {
  responseType?: 'dialogue' | 'feedback'
}

export async function streamChat(
  baseUrl: string,
  body: StreamChatBody,
): Promise<Response> {
  const response = await fetch(`${baseUrl}/api/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Chat failed (${response.status}): ${text}`)
  }

  return response
}

export async function startSession(baseUrl: string): Promise<SessionStartResponse> {
  const response = await fetch(`${baseUrl}/api/session/start`, {
    method: 'POST',
  })
  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Session start failed (${response.status}): ${text}`)
  }
  return response.json() as Promise<SessionStartResponse>
}

export async function streamSessionMessage(
  baseUrl: string,
  sessionId: string,
  body: SessionMessageBody,
): Promise<Response> {
  const response = await fetch(`${baseUrl}/api/session/${sessionId}/message`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Session message failed (${response.status}): ${text}`)
  }
  return response
}

export async function streamSessionFeedback(
  baseUrl: string,
  sessionId: string,
  model: string,
): Promise<Response> {
  const response = await fetch(`${baseUrl}/api/session/${sessionId}/feedback`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ model }),
  })
  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Session feedback failed (${response.status}): ${text}`)
  }
  return response
}

export async function getLearnerMemory(baseUrl: string): Promise<{ focusAreas: Array<{ errorKey: string; hint: string }> }> {
  const response = await fetch(`${baseUrl}/api/learner-memory`)
  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Memory fetch failed (${response.status}): ${text}`)
  }
  return response.json() as Promise<{ focusAreas: Array<{ errorKey: string; hint: string }> }>
}

export async function resetLearnerMemory(baseUrl: string): Promise<void> {
  const response = await fetch(`${baseUrl}/api/learner-memory`, { method: 'DELETE' })
  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Memory reset failed (${response.status}): ${text}`)
  }
}

export async function synthesizeSpeech(
  baseUrl: string,
  options: {
    input: string
    model?: string
    voice?: string
    format?: string
    signal?: AbortSignal
  },
): Promise<Blob> {
  const response = await fetch(`${baseUrl}/api/speech`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      input: options.input,
      model: options.model,
      voice: options.voice,
      format: options.format ?? 'mp3',
    }),
    signal: options.signal,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Speech failed (${response.status}): ${text}`)
  }

  return response.blob()
}

export async function synthesizeSpeechStream(
  baseUrl: string,
  options: {
    input: string
    model?: string
    voice?: string
    format?: string
    signal?: AbortSignal
  },
): Promise<Response> {
  const response = await fetch(`${baseUrl}/api/speech`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      input: options.input,
      model: options.model,
      voice: options.voice,
      format: options.format ?? 'mp3',
    }),
    signal: options.signal,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Speech failed (${response.status}): ${text}`)
  }

  return response
}

export async function transcribeSpeech(
  baseUrl: string,
  options: {
    audio: Blob
    filename?: string
    model?: string
  },
): Promise<string> {
  const form = new FormData()
  form.append('audio', options.audio, options.filename ?? 'voice-message.webm')
  if (options.model) form.append('model', options.model)

  const response = await fetch(`${baseUrl}/api/transcribe`, {
    method: 'POST',
    body: form,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(`Transcription failed (${response.status}): ${text}`)
  }

  const payload = (await response.json()) as { text?: string }
  return (payload.text ?? '').trim()
}

export async function* parseSSEStream(
  response: Response,
): AsyncGenerator<{ chunk?: string; meta?: SessionStreamMeta }> {
  const reader = response.body!.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        const trimmed = line.trim()
        if (!trimmed.startsWith('data: ')) continue
        const data = trimmed.slice(6)
        if (data === '[DONE]') return

        try {
          const parsed = JSON.parse(data)
          if (trimmed.startsWith('event: meta')) continue
          const responseType = parsed.responseType
          if (responseType === 'dialogue' || responseType === 'feedback') {
            yield { meta: { responseType } }
            continue
          }
          const content = parsed.choices?.[0]?.delta?.content
          if (content) yield { chunk: content }
        } catch {
          // skip malformed chunks
        }
      }
    }
  } finally {
    reader.releaseLock()
  }
}
