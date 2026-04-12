<script setup lang="ts">
import { ref, onUnmounted, watch } from 'vue'
import type { ChatMessage } from '../types'
import { useChatStore } from '../stores/chat'
import { useSettingsStore } from '../stores/settings'
import { synthesizeSpeech } from '../services/api'
import VoiceSchematicPlayer from './VoiceSchematicPlayer.vue'

const props = defineProps<{ message: ChatMessage }>()
const chatStore = useChatStore()
const settings = useSettingsStore()

const isSpeaking = ref(false)
const speakAbort = ref<AbortController | null>(null)
let ttsObjectUrl: string | null = null
let currentAudio: HTMLAudioElement | null = null

function stripForTts(text: string): string {
  const max = 3900
  const t = text.trim()
  if (t.length <= max) return t
  return `${t.slice(0, max)}…`
}

function revokeTtsUrl() {
  if (ttsObjectUrl) {
    URL.revokeObjectURL(ttsObjectUrl)
    ttsObjectUrl = null
  }
}

function cleanupSpeaking() {
  isSpeaking.value = false
  speakAbort.value = null
  if (currentAudio) {
    currentAudio.pause()
    currentAudio = null
  }
  revokeTtsUrl()
}

function stopSpeaking() {
  speakAbort.value?.abort()
  cleanupSpeaking()
}

async function playTts() {
  const raw = props.message.content.trim()
  if (!raw) return

  stopSpeaking()

  const ac = new AbortController()
  speakAbort.value = ac
  isSpeaking.value = true

  try {
    const blob = await synthesizeSpeech(settings.apiBaseUrl, {
      input: stripForTts(raw),
      model: settings.ttsModel,
      voice: settings.ttsVoice,
      format: 'mp3',
      signal: ac.signal,
    })
    if (ac.signal.aborted) {
      cleanupSpeaking()
      return
    }
    revokeTtsUrl()
    ttsObjectUrl = URL.createObjectURL(blob)
    if (ac.signal.aborted) {
      cleanupSpeaking()
      return
    }
    const audio = new Audio(ttsObjectUrl)
    currentAudio = audio
    audio.onended = () => cleanupSpeaking()
    audio.onerror = () => cleanupSpeaking()
    await audio.play()
  } catch (e) {
    const name = e instanceof DOMException ? e.name : (e as Error)?.name
    if (name === 'AbortError') {
      cleanupSpeaking()
      return
    }
    console.error(e)
    cleanupSpeaking()
  }
}

async function speak() {
  if (isSpeaking.value) {
    stopSpeaking()
    return
  }
  await playTts()
}

watch(
  () => chatStore.isStreaming,
  (streaming, wasStreaming) => {
    if (wasStreaming !== true || streaming !== false) return
    if (props.message.role !== 'assistant') return
    const msgs = chatStore.messages
    const last = msgs[msgs.length - 1]
    if (!last || last.id !== props.message.id) return
    if (!props.message.content.trim()) return
    void playTts()
  },
)

onUnmounted(() => {
  stopSpeaking()
})

const isUserVoiceBubble = () =>
  props.message.role === 'user'
  && props.message.source === 'voice'
  && !!props.message.audioUrl
</script>

<template>
  <div :class="['bubble', `bubble--${message.role}`]">
    <div
      class="bubble-content"
      :class="{ 'bubble-content--voice-user': isUserVoiceBubble() }"
    >
      <template v-if="isUserVoiceBubble()">
        <VoiceSchematicPlayer
          :src="message.audioUrl!"
          variant="bubble-user"
        />
      </template>
      <template v-else>
        <span v-if="message.content" class="bubble-text">{{
          message.content
        }}</span>
        <span
          v-if="!message.content && message.role === 'assistant'"
          class="bubble-loading"
        >
          <span class="dot" /><span class="dot" /><span class="dot" />
        </span>
      </template>
    </div>

    <div
      v-if="message.role === 'assistant' && message.content"
      class="bubble-actions"
    >
      <button
        class="bubble-action-btn"
        @click="speak()"
        :title="isSpeaking ? 'Stop' : 'Listen (OpenAI TTS)'"
      >
        {{ isSpeaking ? '⏹' : '🔊' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.bubble {
  max-width: min(75%, 600px);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.bubble--user {
  align-self: flex-end;
}

.bubble--assistant {
  align-self: flex-start;
}

.bubble-content {
  padding: 10px 14px;
  border-radius: var(--radius);
  font-size: 15px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

.bubble-content--voice-user {
  min-width: min(100%, 260px);
  padding-top: 8px;
  padding-bottom: 8px;
}

.bubble--user .bubble-content {
  background: var(--accent);
  color: var(--text-inverse);
  border-bottom-right-radius: 4px;
}

.bubble--assistant .bubble-content {
  background: var(--surface);
  color: var(--text);
  border-bottom-left-radius: 4px;
  box-shadow: var(--shadow);
}

.bubble-actions {
  display: flex;
  gap: 4px;
  padding: 0 4px;
}

.bubble--user .bubble-actions {
  justify-content: flex-end;
}

.bubble-action-btn {
  font-size: 14px;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
  color: var(--text-secondary);
  transition: background var(--transition);
  line-height: 1;
}

.bubble-action-btn:hover {
  background: var(--surface-alt);
}

.bubble-loading {
  display: inline-flex;
  gap: 4px;
  padding: 2px 0;
}

.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--text-secondary);
  animation: bounce 1.2s infinite;
}

.dot:nth-child(2) {
  animation-delay: 0.2s;
}
.dot:nth-child(3) {
  animation-delay: 0.4s;
}

@keyframes bounce {
  0%,
  80%,
  100% {
    opacity: 0.3;
  }
  40% {
    opacity: 1;
  }
}
</style>
