<script setup lang="ts">
import { ref, watch, onUnmounted } from 'vue'

const props = defineProps<{
  src: string
  /** `composer` — поле ввода; `bubble-user` — пузырёк пользователя (тёмный фон) */
  variant: 'composer' | 'bubble-user'
}>()

const audioEl = ref<HTMLAudioElement | null>(null)
const playing = ref(false)
const durationSec = ref<number | null>(null)

const waveHeights = Array.from({ length: 24 }, (_, i) => {
  return 5 + ((i * 17 + (i % 5) * 9) % 20)
})

const durationLabel = ref('—:—')

function updateLabel() {
  const s = durationSec.value
  if (s == null || !Number.isFinite(s) || s <= 0) {
    durationLabel.value = '—:—'
    return
  }
  const m = Math.floor(s / 60)
  const sec = Math.floor(s % 60)
  durationLabel.value = `${m}:${sec.toString().padStart(2, '0')}`
}

function applyDurationFromElement() {
  const a = audioEl.value
  if (!a) return
  const d = a.duration
  if (Number.isFinite(d) && d > 0 && !Number.isNaN(d)) {
    durationSec.value = d
    updateLabel()
  }
}

/** WebM от MediaRecorder часто не отдаёт duration в metadata — декодируем через Web Audio. */
let durationProbeId = 0
watch(
  () => props.src,
  async (url) => {
    const id = ++durationProbeId
    durationSec.value = null
    playing.value = false
    durationLabel.value = '—:—'
    if (!url) return
    try {
      const res = await fetch(url)
      const buf = await res.arrayBuffer()
      if (id !== durationProbeId) return
      const ctx = new AudioContext()
      try {
        const decoded = await ctx.decodeAudioData(buf.slice(0))
        if (id !== durationProbeId) return
        if (
          decoded.duration > 0
          && Number.isFinite(decoded.duration)
        ) {
          durationSec.value = decoded.duration
          updateLabel()
        }
      } finally {
        await ctx.close()
      }
    } catch {
      // ниже — fallback через <audio>
    }
  },
  { immediate: true },
)

function onLoadedMeta() {
  if (durationSec.value != null && durationSec.value > 0) return
  applyDurationFromElement()
}

function onDurationChange() {
  if (durationSec.value != null && durationSec.value > 0) return
  applyDurationFromElement()
}

function onEnded() {
  playing.value = false
  if (audioEl.value) audioEl.value.currentTime = 0
}

async function togglePlay() {
  const a = audioEl.value
  if (!a) return
  if (playing.value) {
    a.pause()
    a.currentTime = 0
    playing.value = false
    return
  }
  try {
    await a.play()
    playing.value = true
  } catch {
    playing.value = false
  }
}

onUnmounted(() => {
  audioEl.value?.pause()
})
</script>

<template>
  <div
    class="voice-chip"
    :class="{
      'voice-chip--composer': variant === 'composer',
      'voice-chip--bubble-user': variant === 'bubble-user',
    }"
  >
    <audio
      ref="audioEl"
      :src="src"
      preload="auto"
      class="voice-chip-audio"
      @loadedmetadata="onLoadedMeta"
      @durationchange="onDurationChange"
      @ended="onEnded"
    />
    <button
      type="button"
      class="voice-chip-play"
      :class="{ active: playing }"
      @click="togglePlay"
      :title="playing ? 'Stop' : 'Play'"
    >
      <svg
        v-if="!playing"
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="currentColor"
      >
        <path d="M8 5v14l11-7z" />
      </svg>
      <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="currentColor">
        <rect x="6" y="5" width="4" height="14" rx="1" />
        <rect x="14" y="5" width="4" height="14" rx="1" />
      </svg>
    </button>
    <div
      class="voice-chip-wave"
      :class="{ 'voice-chip-wave--playing': playing }"
      aria-hidden="true"
    >
      <span
        v-for="(h, i) in waveHeights"
        :key="i"
        class="voice-chip-bar"
        :style="{ height: `${h}px` }"
      />
    </div>
    <span class="voice-chip-time">{{ durationLabel }}</span>
  </div>
</template>

<style scoped>
.voice-chip {
  position: relative;
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  flex: 1;
}

.voice-chip-audio {
  position: absolute;
  width: 0;
  height: 0;
  opacity: 0;
  pointer-events: none;
}

.voice-chip-play {
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  transition:
    opacity var(--transition),
    background var(--transition);
}

.voice-chip--composer .voice-chip-play {
  background: var(--accent);
  color: var(--text-inverse);
}

.voice-chip--composer .voice-chip-play.active {
  background: var(--text-secondary);
}

.voice-chip--bubble-user .voice-chip-play {
  background: rgba(255, 255, 255, 0.25);
  color: var(--text-inverse);
}

.voice-chip--bubble-user .voice-chip-play:hover {
  background: rgba(255, 255, 255, 0.35);
}

.voice-chip--bubble-user .voice-chip-play.active {
  background: rgba(255, 255, 255, 0.45);
}

.voice-chip-play:hover {
  opacity: 0.92;
}

.voice-chip-wave {
  flex: 1;
  min-width: 0;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 2px;
  padding: 0 4px;
}

.voice-chip-bar {
  flex: 1;
  min-width: 2px;
  max-width: 4px;
  border-radius: 2px;
  align-self: center;
  transition:
    opacity 0.15s ease,
    transform 0.15s ease;
}

.voice-chip--composer .voice-chip-bar {
  background: var(--accent);
  opacity: 0.45;
}

.voice-chip--bubble-user .voice-chip-bar {
  background: rgba(255, 255, 255, 0.85);
  opacity: 0.55;
}

.voice-chip-wave--playing .voice-chip-bar {
  opacity: 0.95;
  animation: voice-bar-pulse 0.8s ease-in-out infinite;
}

.voice-chip-wave--playing .voice-chip-bar:nth-child(4n + 1) {
  animation-delay: 0s;
}
.voice-chip-wave--playing .voice-chip-bar:nth-child(4n + 2) {
  animation-delay: 0.1s;
}
.voice-chip-wave--playing .voice-chip-bar:nth-child(4n + 3) {
  animation-delay: 0.2s;
}
.voice-chip-wave--playing .voice-chip-bar:nth-child(4n) {
  animation-delay: 0.3s;
}

@keyframes voice-bar-pulse {
  0%,
  100% {
    transform: scaleY(0.55);
    opacity: 0.5;
  }
  50% {
    transform: scaleY(1);
    opacity: 1;
  }
}

.voice-chip-time {
  flex-shrink: 0;
  font-size: 13px;
  font-variant-numeric: tabular-nums;
  min-width: 2.25rem;
  text-align: right;
}

.voice-chip--composer .voice-chip-time {
  color: var(--text-secondary);
}

.voice-chip--bubble-user .voice-chip-time {
  color: rgba(255, 255, 255, 0.85);
}
</style>
