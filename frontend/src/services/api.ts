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

export async function* parseSSEStream(response: Response): AsyncGenerator<string> {
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
          const content = parsed.choices?.[0]?.delta?.content
          if (content) yield content
        } catch {
          // skip malformed chunks
        }
      }
    }
  } finally {
    reader.releaseLock()
  }
}
