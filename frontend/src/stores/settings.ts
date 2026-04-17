import { ref, watch } from 'vue'
import { defineStore } from 'pinia'

const DEFAULT_SYSTEM_PROMPT = `You are Voxsy, a friendly English conversation partner and tutor. Your goal is to help the user speak fluent, grammatically correct, and easy-to-understand English.

Rules:
- Respond primarily in English.
- Keep the dialogue natural first; avoid long correction blocks on every turn.
- When the user sends audio, pay attention to pronunciation, rhythm, pauses, hesitations, and fillers (like "uh", "umm", "mmm", "eee"), especially when they reduce clarity.
- If you detect a serious mistake, explain not only the correction but also why it is wrong in simple terms.
- Keep corrections encouraging and concise.
- Adapt your vocabulary to the user's apparent level.`

const LS_VOICE_CHAT_MODEL = 'voxsy_chatModel'
const LS_TEXT_CHAT_MODEL = 'voxsy_textChatModel'
const LS_TTS_MODEL = 'voxsy_ttsModel'
const LS_TTS_VOICE = 'voxsy_ttsVoice'

export const useSettingsStore = defineStore('settings', () => {
  const systemPrompt = ref(
    localStorage.getItem('voxsy_systemPrompt') ?? DEFAULT_SYSTEM_PROMPT,
  )
  const apiBaseUrl = ref(
    localStorage.getItem('voxsy_apiBaseUrl')
    ?? import.meta.env.VITE_API_BASE_URL
    ?? 'http://localhost:5041',
  )
  const chatModel = ref(
    localStorage.getItem(LS_VOICE_CHAT_MODEL) ?? 'gpt-audio-mini',
  )
  const textChatModel = ref(
    localStorage.getItem(LS_TEXT_CHAT_MODEL) ?? 'gpt-4o-mini',
  )
  const ttsModel = ref(localStorage.getItem(LS_TTS_MODEL) ?? 'gpt-4o-mini-tts')
  const ttsVoice = ref(localStorage.getItem(LS_TTS_VOICE) ?? 'alloy')

  watch(systemPrompt, (v) => localStorage.setItem('voxsy_systemPrompt', v))
  watch(apiBaseUrl, (v) => localStorage.setItem('voxsy_apiBaseUrl', v))
  watch(chatModel, (v) => localStorage.setItem(LS_VOICE_CHAT_MODEL, v))
  watch(textChatModel, (v) => localStorage.setItem(LS_TEXT_CHAT_MODEL, v))
  watch(ttsModel, (v) => localStorage.setItem(LS_TTS_MODEL, v))
  watch(ttsVoice, (v) => localStorage.setItem(LS_TTS_VOICE, v))

  function resetPrompt() {
    systemPrompt.value = DEFAULT_SYSTEM_PROMPT
  }

  return {
    systemPrompt,
    apiBaseUrl,
    chatModel,
    textChatModel,
    ttsModel,
    ttsVoice,
    resetPrompt,
  }
})
