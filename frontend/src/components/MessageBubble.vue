<script setup lang="ts">
import { ref } from 'vue'
import type { ChatMessage } from '../types'

const props = defineProps<{ message: ChatMessage }>()

const isSpeaking = ref(false)

function speak() {
  speechSynthesis.cancel()
  const utterance = new SpeechSynthesisUtterance(props.message.content)
  utterance.lang = 'en-US'
  utterance.rate = 0.9
  utterance.onend = () => {
    isSpeaking.value = false
  }
  utterance.onerror = () => {
    isSpeaking.value = false
  }
  isSpeaking.value = true
  speechSynthesis.speak(utterance)
}

function stopSpeaking() {
  speechSynthesis.cancel()
  isSpeaking.value = false
}

const isPlaying = ref(false)
let currentAudio: HTMLAudioElement | null = null

function playRecording() {
  if (!props.message.audioUrl) return

  if (currentAudio) {
    currentAudio.pause()
    currentAudio = null
    isPlaying.value = false
    return
  }

  currentAudio = new Audio(props.message.audioUrl)
  currentAudio.onended = () => {
    isPlaying.value = false
    currentAudio = null
  }
  currentAudio.onerror = () => {
    isPlaying.value = false
    currentAudio = null
  }
  isPlaying.value = true
  currentAudio.play()
}
</script>

<template>
  <div :class="['bubble', `bubble--${message.role}`]">
    <div class="bubble-content">
      <span v-if="message.source === 'voice'" class="bubble-source">🎤</span>
      <span v-if="message.content" class="bubble-text">{{
        message.content
      }}</span>
      <span
        v-if="!message.content && message.role === 'assistant'"
        class="bubble-loading"
      >
        <span class="dot" /><span class="dot" /><span class="dot" />
      </span>
    </div>

    <div v-if="message.content" class="bubble-actions">
      <button
        v-if="message.role === 'assistant'"
        class="bubble-action-btn"
        @click="isSpeaking ? stopSpeaking() : speak()"
        :title="isSpeaking ? 'Stop' : 'Listen'"
      >
        {{ isSpeaking ? '⏹' : '🔊' }}
      </button>
      <button
        v-if="message.audioUrl"
        class="bubble-action-btn"
        @click="playRecording"
        :title="isPlaying ? 'Stop playback' : 'Replay recording'"
      >
        {{ isPlaying ? '⏹' : '▶️' }}
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

.bubble-source {
  margin-right: 4px;
  font-size: 13px;
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
