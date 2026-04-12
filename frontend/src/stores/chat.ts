import { ref } from 'vue'
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

export const useChatStore = defineStore('chat', () => {
  const settings = useSettingsStore()
  const messages = ref<ChatMessage[]>([])
  const isStreaming = ref(false)

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
