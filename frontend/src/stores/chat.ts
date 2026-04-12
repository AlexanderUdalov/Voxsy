import { ref } from 'vue'
import { defineStore } from 'pinia'
import type { ChatMessage } from '../types'
import { useSettingsStore } from './settings'
import { streamChat, parseSSEStream } from '../services/api'

export const useChatStore = defineStore('chat', () => {
  const settings = useSettingsStore()
  const messages = ref<ChatMessage[]>([])
  const isStreaming = ref(false)

  async function sendMessage(
    content: string,
    source: 'text' | 'voice' = 'text',
    audioUrl?: string,
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
      const apiMessages = [
        { role: 'system', content: settings.systemPrompt },
        ...messages.value
          .slice(0, -1)
          .map((m) => ({
            role: m.role,
            content:
              m.source === 'voice'
                ? `[Transcribed from voice] ${m.content}`
                : m.content,
          })),
      ]

      const response = await streamChat(settings.apiBaseUrl, apiMessages)

      for await (const chunk of parseSSEStream(response)) {
        messages.value[assistantIdx].content += chunk
      }
    } catch (error) {
      messages.value[assistantIdx].content =
        `Error: ${error instanceof Error ? error.message : String(error)}`
    } finally {
      isStreaming.value = false
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
