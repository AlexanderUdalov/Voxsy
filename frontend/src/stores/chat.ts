import { ref, watch } from 'vue'
import { defineStore } from 'pinia'
import type { ChatMessage } from '../types'
import { useSettingsStore } from './settings'
import {
  startSession,
  streamSessionFeedback,
  streamSessionMessage,
  parseSSEStream,
  transcribeSpeech,
} from '../services/api'
import { usesNativeAudioInput } from '../lib/models'
import { blobToWavBase64 } from '../utils/audio'

const LS_CHAT_MESSAGES = 'voxsy_chatMessages'
const LS_SESSION_ID = 'voxsy_sessionId'

function trimTrailingEmptyAssistant(msgs: ChatMessage[]): ChatMessage[] {
  const out = [...msgs]
  while (
    out.length > 0
    && out[out.length - 1]!.role === 'assistant'
    && !out[out.length - 1]!.content.trim()
  ) {
    out.pop()
  }
  return out
}

function loadPersistedMessages(): ChatMessage[] {
  try {
    const raw = localStorage.getItem(LS_CHAT_MESSAGES)
    if (!raw) return []
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return []
    const out: ChatMessage[] = []
    for (const item of parsed) {
      if (!item || typeof item !== 'object') continue
      const o = item as Record<string, unknown>
      if (o.role !== 'user' && o.role !== 'assistant') continue
      const id = typeof o.id === 'string' ? o.id : crypto.randomUUID()
      let content = typeof o.content === 'string' ? o.content : ''
      const source =
        o.source === 'voice' || o.source === 'text' ? o.source : undefined
      const responseType =
        o.responseType === 'dialogue' || o.responseType === 'feedback'
          ? o.responseType
          : undefined
      const msg: ChatMessage = { id, role: o.role, content }
      if (source) msg.source = source
      if (responseType) msg.responseType = responseType
      if (msg.role === 'user' && msg.source === 'voice' && !content.trim()) {
        content = '(Voice message)'
        msg.content = content
      }
      out.push(msg)
    }
    return trimTrailingEmptyAssistant(out)
  } catch {
    return []
  }
}

function persistMessages(msgs: ChatMessage[]) {
  if (msgs.length === 0) {
    localStorage.removeItem(LS_CHAT_MESSAGES)
    return
  }
  const serializable = msgs.map(({ id, role, content, source, responseType }) => ({
    id,
    role,
    content,
    ...(source ? { source } : {}),
    ...(responseType ? { responseType } : {}),
  }))
  localStorage.setItem(LS_CHAT_MESSAGES, JSON.stringify(serializable))
}

export const useChatStore = defineStore('chat', () => {
  const settings = useSettingsStore()
  const messages = ref<ChatMessage[]>(loadPersistedMessages())
  const isStreaming = ref(false)
  const sessionId = ref<string | null>(localStorage.getItem(LS_SESSION_ID))

  watch(
    messages,
    (msgs) => {
      if (isStreaming.value) return
      persistMessages(msgs)
    },
    { deep: true },
  )

  async function ensureSession() {
    if (sessionId.value) return sessionId.value
    const started = await startSession(settings.apiBaseUrl)
    sessionId.value = started.sessionId
    localStorage.setItem(LS_SESSION_ID, started.sessionId)
    return started.sessionId
  }

  async function sendMessage(
    content: string,
    source: 'text' | 'voice' = 'text',
    audioUrl?: string,
    voiceBlob?: Blob | null,
  ) {
    const sid = await ensureSession()

    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: 'user',
      content: source === 'voice' ? '' : content,
      source,
      audioUrl,
    }
    messages.value.push(userMessage)

    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: '',
    })

    const assistantIdx = messages.value.length - 1
    const userIdx = assistantIdx - 1
    isStreaming.value = true

    try {
      let contentToSend = content
      let audioBase64: string | undefined
      if (source === 'voice') {
        if (!voiceBlob || !usesNativeAudioInput(settings.chatModel)) {
          messages.value[assistantIdx].content =
            'Error: Voice requires an audio-capable chat model and a recording.'
          return
        }

        try {
          const transcript = await transcribeSpeech(settings.apiBaseUrl, {
            audio: voiceBlob,
            filename: 'voice-message.webm',
          })
          messages.value[userIdx].content = transcript
          contentToSend = transcript
        } catch (e) {
          console.error('Could not transcribe voice:', e)
          messages.value[userIdx].content = ''
          contentToSend = ''
        }
        if (!contentToSend.trim()) {
          messages.value[assistantIdx].content =
            'Error: Could not transcribe audio. Try recording again.'
          return
        }
        try {
          audioBase64 = await blobToWavBase64(voiceBlob)
        } catch (e) {
          console.error('Could not encode voice:', e)
          messages.value[assistantIdx].content =
            'Error: Could not encode voice audio. Try recording again.'
          return
        }
      }

      const requestModel =
        source === 'voice' ? settings.chatModel : settings.textChatModel
      const response = await streamSessionMessage(settings.apiBaseUrl, sid, {
        content: contentToSend.trim(),
        source,
        model: requestModel,
        ...(audioBase64 ? { audioBase64, audioFormat: 'wav' } : {}),
      })

      for await (const payload of parseSSEStream(response)) {
        if (payload.meta?.responseType) {
          messages.value[assistantIdx].responseType = payload.meta.responseType
          continue
        }
        if (!payload.chunk) continue
        messages.value[assistantIdx].content += payload.chunk
      }
    } catch (error) {
      messages.value[assistantIdx].content =
        `Error: ${error instanceof Error ? error.message : String(error)}`
    } finally {
      isStreaming.value = false
      persistMessages(messages.value)
    }
  }

  async function requestFeedback() {
    if (isStreaming.value || messages.value.length === 0) return
    const sid = await ensureSession()

    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: '',
      responseType: 'feedback',
    })
    const assistantIdx = messages.value.length - 1
    isStreaming.value = true

    try {
      const response = await streamSessionFeedback(
        settings.apiBaseUrl,
        sid,
        settings.textChatModel,
      )
      for await (const payload of parseSSEStream(response)) {
        if (payload.meta?.responseType) {
          messages.value[assistantIdx].responseType = payload.meta.responseType
          continue
        }
        if (!payload.chunk) continue
        messages.value[assistantIdx].content += payload.chunk
      }
    } catch (error) {
      messages.value[assistantIdx].content =
        `Error: ${error instanceof Error ? error.message : String(error)}`
    } finally {
      isStreaming.value = false
      persistMessages(messages.value)
    }
  }

  function clearMessages() {
    for (const m of messages.value) {
      if (m.audioUrl) URL.revokeObjectURL(m.audioUrl)
    }
    messages.value = []
    sessionId.value = null
    localStorage.removeItem(LS_SESSION_ID)
  }

  return {
    messages,
    isStreaming,
    sessionId,
    sendMessage,
    requestFeedback,
    clearMessages,
  }
})

