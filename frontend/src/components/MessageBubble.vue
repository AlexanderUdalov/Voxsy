<script setup lang="ts">
import { ref, onUnmounted, watch } from 'vue'
import type { ChatMessage } from '../types'
import { useChatStore } from '../stores/chat'
import { useSettingsStore } from '../stores/settings'
import { synthesizeSpeechStream } from '../services/api'
import VoiceSchematicPlayer from './VoiceSchematicPlayer.vue'

const props = defineProps<{ message: ChatMessage }>()
const chatStore = useChatStore()
const settings = useSettingsStore()

type TtsState = 'idle' | 'loading' | 'playing'
const ttsState = ref<TtsState>('idle')
const speakAbort = ref<AbortController | null>(null)
let ttsObjectUrl: string | null = null
let currentAudio: HTMLAudioElement | null = null

function canStreamMp3WithMediaSource(): boolean {
  return (
    typeof window !== 'undefined'
    && 'MediaSource' in window
    && MediaSource.isTypeSupported('audio/mpeg')
  )
}

function wireAudioLifecycle(audio: HTMLAudioElement) {
  audio.onended = () => cleanupSpeaking()
  audio.onerror = () => cleanupSpeaking()
  audio.onplaying = () => {
    ttsState.value = 'playing'
  }
}

function createAbortError(): Error {
  return new DOMException('The operation was aborted.', 'AbortError')
}

async function streamToMediaSource(
  response: Response,
  audio: HTMLAudioElement,
  signal: AbortSignal,
) {
  if (!response.body) throw new Error('Speech stream is unavailable')

  const mediaSource = new MediaSource()
  revokeTtsUrl()
  ttsObjectUrl = URL.createObjectURL(mediaSource)
  audio.src = ttsObjectUrl

  let sourceBuffer: SourceBuffer | null = null
  const queue: ArrayBuffer[] = []
  let streamEnded = false

  const appendNext = () => {
    if (!sourceBuffer || sourceBuffer.updating || queue.length === 0) return
    sourceBuffer.appendBuffer(queue.shift()!)
  }

  const maybeCloseMediaSource = () => {
    if (!sourceBuffer || sourceBuffer.updating || queue.length > 0) return
    if (streamEnded && mediaSource.readyState === 'open') {
      try {
        mediaSource.endOfStream()
      } catch {
        // ignore endOfStream races
      }
    }
  }

  await new Promise<void>((resolve, reject) => {
    const onAbort = () => reject(createAbortError())
    if (signal.aborted) {
      reject(createAbortError())
      return
    }
    signal.addEventListener('abort', onAbort, { once: true })
    mediaSource.addEventListener(
      'sourceopen',
      () => {
        signal.removeEventListener('abort', onAbort)
        sourceBuffer = mediaSource.addSourceBuffer('audio/mpeg')
        sourceBuffer.mode = 'sequence'
        sourceBuffer.addEventListener('updateend', () => {
          appendNext()
          maybeCloseMediaSource()
        })
        resolve()
      },
      { once: true },
    )
  })

  const reader = response.body.getReader()
  try {
    while (true) {
      if (signal.aborted) throw createAbortError()
      const { done, value } = await reader.read()
      if (done) break
      if (!value || value.byteLength === 0) continue
      const chunk = new Uint8Array(value.byteLength)
      chunk.set(value)
      queue.push(chunk.buffer)
      appendNext()
    }
    streamEnded = true
    maybeCloseMediaSource()
  } finally {
    reader.releaseLock()
  }
}

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
  ttsState.value = 'idle'
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
  ttsState.value = 'loading'

  try {
    const response = await synthesizeSpeechStream(settings.apiBaseUrl, {
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
    const audio = new Audio()
    currentAudio = audio
    wireAudioLifecycle(audio)

    if (canStreamMp3WithMediaSource()) {
      const pumping = streamToMediaSource(response, audio, ac.signal)
      await audio.play()
      void pumping.catch((e) => {
        const name = e instanceof DOMException ? e.name : (e as Error)?.name
        if (name === 'AbortError') return
        console.error(e)
        cleanupSpeaking()
      })
      return
    }

    // Fallback for browsers that cannot append MPEG chunks via MediaSource.
    const blob = await response.blob()
    revokeTtsUrl()
    ttsObjectUrl = URL.createObjectURL(blob)
    audio.src = ttsObjectUrl
    if (ac.signal.aborted) {
      cleanupSpeaking()
      return
    }
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
  if (ttsState.value !== 'idle') {
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
        <p v-if="message.content" class="voice-transcript">
          {{ message.content }}
        </p>
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
        :title="
          ttsState === 'playing'
            ? 'Stop'
            : ttsState === 'loading'
              ? 'Buffering audio... click to stop'
              : 'Listen (OpenAI TTS)'
        "
      >
        {{
          ttsState === 'playing'
            ? '⏹'
            : ttsState === 'loading'
              ? '⏳'
              : '🔊'
        }}
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
  display: flex;
  flex-direction: column;
  gap: 6px;
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

.voice-transcript {
  margin: 0;
  font-size: 12px;
  line-height: 1.35;
  color: color-mix(in srgb, var(--text-inverse) 65%, transparent);
  font-style: italic;
  white-space: pre-wrap;
  word-break: break-word;
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
