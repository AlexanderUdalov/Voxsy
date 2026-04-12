import { ref, watch } from 'vue'
import { defineStore } from 'pinia'
import type { ChatMessage } from '../types'
import { useSettingsStore } from './settings'
import {
  streamChat,
  parseSSEStream,
  type ChatApiMessage,
  type ChatContentPart,
} from '../services/api'
import { usesNativeAudioInput } from '../lib/models'
import { blobToWavBase64 } from '../utils/audio'

function toApiMessage(
  m: ChatMessage,
  isLatestUserVoice: boolean,
  wavBase64: string | undefined,
  chatModel: string,
): ChatApiMessage {
  if (
    m.role === 'user'
    && m.source === 'voice'
    && isLatestUserVoice
    && wavBase64
    && usesNativeAudioInput(chatModel)
  ) {
    const parts: ChatContentPart[] = [
      {
        type: 'text',
        text: 'The user sent a voice message while practicing English. Listen and respond naturally; give brief pronunciation or grammar feedback when helpful.',
      },
      { type: 'input_audio', input_audio: { data: wavBase64, format: 'wav' } },
    ]
    return { role: 'user', content: parts }
  }
  if (m.role === 'user' && m.source === 'voice') {
    return {
      role: 'user',
      content:
        '[Earlier voice message from the user — raw audio is not re-sent in this request.]',
    }
  }
  return { role: m.role, content: m.content }
}

const LS_CHAT_MESSAGES = 'voxsy_chatMessages'

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
      const msg: ChatMessage = { id, role: o.role, content }
      if (source) msg.source = source
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
  const serializable = msgs.map(({ id, role, content, source }) => ({
    id,
    role,
    content,
    ...(source ? { source } : {}),
  }))
  localStorage.setItem(LS_CHAT_MESSAGES, JSON.stringify(serializable))
}

export const useChatStore = defineStore('chat', () => {
  const settings = useSettingsStore()
  const messages = ref<ChatMessage[]>(loadPersistedMessages())
  const isStreaming = ref(false)

  watch(
    messages,
    (msgs) => {
      if (isStreaming.value) return
      persistMessages(msgs)
    },
    { deep: true },
  )

  async function sendMessage(
    content: string,
    source: 'text' | 'voice' = 'text',
    audioUrl?: string,
    voiceBlob?: Blob | null,
  ) {
    messages.value.push({
      id: crypto.randomUUID(),
      role: 'user',
      content,
      source,
      audioUrl,
    })

    messages.value.push({
      id: crypto.randomUUID(),
      role: 'assistant',
      content: '',
    })

    const assistantIdx = messages.value.length - 1
    isStreaming.value = true

    try {
      const hist = messages.value.slice(0, -1)
      const lastIdx = hist.length - 1

      let voiceWavBase64: string | undefined
      if (source === 'voice') {
        if (!voiceBlob || !usesNativeAudioInput(settings.chatModel)) {
          messages.value[assistantIdx].content =
            'Error: Voice requires an audio-capable chat model and a recording.'
          return
        }
        try {
          voiceWavBase64 = await blobToWavBase64(voiceBlob)
        } catch (e) {
          console.error('Could not encode voice:', e)
        }
        if (!voiceWavBase64) {
          messages.value[assistantIdx].content =
            'Error: Could not prepare audio. Try recording again.'
          return
        }
      }

      const apiMessages: ChatApiMessage[] = [
        { role: 'system', content: settings.systemPrompt },
        ...hist.map((m, idx) =>
          toApiMessage(
            m,
            idx === lastIdx && m.role === 'user' && m.source === 'voice',
            voiceWavBase64,
            settings.chatModel,
          ),
        ),
      ]

      const requestModel =
        source === 'voice' ? settings.chatModel : settings.textChatModel

      const body: Parameters<typeof streamChat>[1] = {
        model: requestModel,
        messages: apiMessages,
      }
      if (source === 'voice' && usesNativeAudioInput(settings.chatModel)) {
        body.modalities = ['text']
      }

      const response = await streamChat(settings.apiBaseUrl, body)

      for await (const chunk of parseSSEStream(response)) {
        messages.value[assistantIdx].content += chunk
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
  }

  return { messages, isStreaming, sendMessage, clearMessages }
})
