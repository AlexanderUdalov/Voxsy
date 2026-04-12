import { ref, watch } from 'vue'
import { defineStore } from 'pinia'

const DEFAULT_SYSTEM_PROMPT = `You are a friendly English conversation partner and tutor. Your goal is to have natural conversations while helping the user improve their English.

Rules:
- Respond primarily in English.
- When the user makes grammar or vocabulary mistakes, first respond to their message naturally. Then add a brief "Correction:" section with 1-2 fixes.
- If a message is marked as [Transcribed from voice] and contains odd words or fragments, infer what the user likely meant and respond to that meaning.
- Keep corrections encouraging and concise.
- Adapt your vocabulary to the user's apparent level.`

export const useSettingsStore = defineStore('settings', () => {
  const systemPrompt = ref(
    localStorage.getItem('voxsy_systemPrompt') ?? DEFAULT_SYSTEM_PROMPT,
  )
  const apiBaseUrl = ref(
    localStorage.getItem('voxsy_apiBaseUrl')
    ?? import.meta.env.VITE_API_BASE_URL
    ?? 'http://localhost:5041',
  )

  watch(systemPrompt, (v) => localStorage.setItem('voxsy_systemPrompt', v))
  watch(apiBaseUrl, (v) => localStorage.setItem('voxsy_apiBaseUrl', v))

  function resetPrompt() {
    systemPrompt.value = DEFAULT_SYSTEM_PROMPT
  }

  return { systemPrompt, apiBaseUrl, resetPrompt }
})
