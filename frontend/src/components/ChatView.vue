<script setup lang="ts">
import { ref, watch, nextTick, onUnmounted, computed } from 'vue'
import { useChatStore } from '../stores/chat'
import { useSettingsStore } from '../stores/settings'
import { usesNativeAudioInput } from '../lib/models'
import MessageBubble from './MessageBubble.vue'
import VoiceSchematicPlayer from './VoiceSchematicPlayer.vue'

const chatStore = useChatStore()
const settingsStore = useSettingsStore()

const messagesEl = ref<HTMLElement>()
const inputEl = ref<HTMLTextAreaElement>()
const inputText = ref('')
const isRecording = ref(false)
const pendingAudioUrl = ref<string | undefined>()
const pendingVoiceBlob = ref<Blob | undefined>()

const voiceInputOk = computed(() => usesNativeAudioInput(settingsStore.chatModel))

const micBlockedByText = computed(() => inputText.value.trim().length > 0)

let mediaRecorder: MediaRecorder | null = null
let audioChunks: Blob[] = []

function scrollToBottom() {
  if (messagesEl.value) {
    messagesEl.value.scrollTop = messagesEl.value.scrollHeight
  }
}

watch(
  () => {
    const msgs = chatStore.messages
    return msgs.length > 0 ? msgs[msgs.length - 1].content : null
  },
  scrollToBottom,
  { flush: 'post' },
)

watch(
  () => chatStore.messages.length,
  scrollToBottom,
  { flush: 'post' },
)

function autoResize() {
  const el = inputEl.value
  if (!el) return
  el.style.height = 'auto'
  el.style.height = `${Math.min(el.scrollHeight, 120)}px`
}

watch(inputText, () => nextTick(autoResize))

function discardVoiceDraft() {
  if (pendingAudioUrl.value) URL.revokeObjectURL(pendingAudioUrl.value)
  pendingAudioUrl.value = undefined
  pendingVoiceBlob.value = undefined
}

async function send() {
  const text = inputText.value.trim()
  const hasVoice = !!pendingVoiceBlob.value
  if ((!text && !hasVoice) || chatStore.isStreaming) return
  if (hasVoice && !voiceInputOk.value) return

  const source = hasVoice ? ('voice' as const) : ('text' as const)
  const audioUrl = pendingAudioUrl.value
  const voiceBlob = pendingVoiceBlob.value

  inputText.value = ''
  pendingAudioUrl.value = undefined
  pendingVoiceBlob.value = undefined

  await chatStore.sendMessage(hasVoice ? '' : text, source, audioUrl, voiceBlob)
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault()
    send()
  }
}

function getSupportedMimeType(): string {
  const types = [
    'audio/webm;codecs=opus',
    'audio/webm',
    'audio/mp4',
    'audio/ogg',
  ]
  return types.find((t) => MediaRecorder.isTypeSupported(t)) ?? ''
}

async function toggleRecording() {
  if (isRecording.value) {
    mediaRecorder?.stop()
    return
  }

  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    const mimeType = getSupportedMimeType()
    const recorder = new MediaRecorder(
      stream,
      mimeType ? { mimeType } : undefined,
    )
    audioChunks = []

    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) audioChunks.push(e.data)
    }

    recorder.onstop = () => {
      stream.getTracks().forEach((t) => t.stop())
      isRecording.value = false
      const blob = new Blob(audioChunks, { type: recorder.mimeType })
      if (pendingAudioUrl.value) URL.revokeObjectURL(pendingAudioUrl.value)
      pendingVoiceBlob.value = blob
      pendingAudioUrl.value = URL.createObjectURL(blob)
    }

    recorder.start()
    mediaRecorder = recorder
    isRecording.value = true
  } catch (err) {
    console.error('Microphone access denied:', err)
  }
}

const micDisabled = computed(
  () =>
    chatStore.isStreaming
    || !voiceInputOk.value
    || !!pendingVoiceBlob.value
    || micBlockedByText.value,
)

const micTitle = computed(() => {
  if (pendingVoiceBlob.value)
    return 'Remove the voice draft to record again'
  if (micBlockedByText.value) return 'Clear the text field to record voice'
  if (!voiceInputOk.value)
    return 'Pick an audio-capable chat model in Settings'
  if (isRecording.value) return 'Stop recording'
  return 'Record voice'
})

onUnmounted(() => {
  if (pendingAudioUrl.value) URL.revokeObjectURL(pendingAudioUrl.value)
  mediaRecorder?.stop()
})
</script>

<template>
  <main class="chat-view">
    <div class="messages" ref="messagesEl">
      <div v-if="chatStore.messages.length === 0" class="empty-state">
        <div class="empty-icon">💬</div>
        <h2>Welcome to Voxsy</h2>
        <p>
          Type a message or record voice (requires an audio chat model in
          Settings). Text and voice cannot be mixed in one message.
        </p>
      </div>
      <MessageBubble
        v-for="msg in chatStore.messages"
        :key="msg.id"
        :message="msg"
      />
    </div>

    <div class="input-area">
      <div class="input-row">
        <div v-if="pendingVoiceBlob && pendingAudioUrl" class="voice-draft">
          <div class="voice-draft-row">
            <VoiceSchematicPlayer
              :src="pendingAudioUrl"
              variant="composer"
            />
            <button
              type="button"
              class="voice-draft-remove"
              @click="discardVoiceDraft"
              title="Remove recording"
            >
              ×
            </button>
          </div>
        </div>
        <textarea
          v-else
          ref="inputEl"
          v-model="inputText"
          @keydown="handleKeydown"
          placeholder="Type a message…"
          rows="1"
          class="text-input"
        />

        <button
          class="action-btn mic-btn"
          :class="{ recording: isRecording }"
          @click="toggleRecording"
          :disabled="micDisabled"
          :title="micTitle"
        >
          <svg
            v-if="!isRecording"
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <rect x="9" y="2" width="6" height="11" rx="3" />
            <path d="M5 10a7 7 0 0 0 14 0" />
            <line x1="12" y1="17" x2="12" y2="22" />
          </svg>
          <svg
            v-else
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="currentColor"
          >
            <rect x="6" y="6" width="12" height="12" rx="2" />
          </svg>
        </button>

        <button
          class="action-btn send-btn"
          @click="send"
          :disabled="
            (!inputText.trim() && !pendingVoiceBlob) ||
            chatStore.isStreaming ||
            (pendingVoiceBlob && !voiceInputOk)
          "
          title="Send"
        >
          <svg
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M22 2 11 13" />
            <path d="M22 2 15 22 11 13 2 9z" />
          </svg>
        </button>
      </div>
    </div>
  </main>
</template>

<style scoped>
.chat-view {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}

.messages {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  text-align: center;
  color: var(--text-secondary);
  gap: 8px;
  padding: 40px 20px;
}

.empty-icon {
  font-size: 48px;
  margin-bottom: 8px;
}

.empty-state h2 {
  font-size: 22px;
  font-weight: 600;
  color: var(--text);
}

.empty-state p {
  font-size: 15px;
  max-width: 320px;
}

.input-area {
  padding: 12px 20px;
  background: var(--surface);
  border-top: 1px solid var(--border);
  flex-shrink: 0;
}

.input-row {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  max-width: 800px;
  margin: 0 auto;
}

.text-input {
  flex: 1;
  resize: none;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 10px 16px;
  font-size: 15px;
  line-height: 1.4;
  background: var(--bg);
  outline: none;
  transition: border-color var(--transition);
  max-height: 120px;
  min-height: 42px;
}

.text-input:focus {
  border-color: var(--accent);
}

.text-input:disabled {
  opacity: 0.6;
}

.voice-draft {
  flex: 1;
  min-width: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  background: var(--bg);
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.voice-draft-row {
  display: flex;
  align-items: center;
  gap: 10px;
  min-height: 36px;
}

.voice-draft-remove {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  border-radius: var(--radius-sm);
  font-size: 22px;
  line-height: 1;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  transition: background var(--transition), color var(--transition);
}

.voice-draft-remove:hover {
  background: var(--surface-alt);
  color: var(--danger);
}

.action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42px;
  height: 42px;
  border-radius: 50%;
  flex-shrink: 0;
  transition:
    background var(--transition),
    opacity var(--transition);
}

.action-btn:disabled {
  opacity: 0.4;
  cursor: default;
}

.mic-btn {
  color: var(--text-secondary);
  background: var(--surface-alt);
}

.mic-btn:hover:not(:disabled) {
  background: var(--border);
}

.mic-btn.recording {
  color: var(--text-inverse);
  background: var(--danger);
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.7;
  }
}

.send-btn {
  color: var(--text-inverse);
  background: var(--accent);
}

.send-btn:hover:not(:disabled) {
  background: var(--accent-hover);
}

@media (max-width: 640px) {
  .messages {
    padding: 12px;
  }

  .input-area {
    padding: 8px 12px;
  }
}
</style>
