<script setup lang="ts">
import { useSettingsStore } from '../stores/settings'
import { useChatStore } from '../stores/chat'
import {
  TEXT_CHAT_MODEL_OPTIONS,
  VOICE_CHAT_MODEL_OPTIONS,
  TTS_MODEL_OPTIONS,
  TTS_VOICE_OPTIONS,
} from '../lib/models'

defineProps<{ open: boolean }>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()

const settings = useSettingsStore()
const chat = useChatStore()

function close() {
  emit('update:open', false)
}
</script>

<template>
  <Transition name="drawer">
    <div v-if="open" class="overlay" @click.self="close">
      <aside class="panel">
        <div class="panel-header">
          <h2>Settings</h2>
          <button class="close-btn" @click="close" title="Close">
            &times;
          </button>
        </div>
        <div class="panel-body">
          <div class="field">
            <label class="field-label">System Prompt</label>
            <textarea
              v-model="settings.systemPrompt"
              class="field-textarea"
              rows="12"
            />
            <button class="link-btn" @click="settings.resetPrompt">
              Reset to default
            </button>
          </div>

          <div class="field">
            <label class="field-label">API Base URL</label>
            <input v-model="settings.apiBaseUrl" class="field-input" />
          </div>

          <div class="field">
            <label class="field-label">Text chat model</label>
            <select v-model="settings.textChatModel" class="field-input">
              <option
                v-for="opt in TEXT_CHAT_MODEL_OPTIONS"
                :key="opt.value"
                :value="opt.value"
              >
                {{ opt.label }}
              </option>
            </select>
            <p class="field-hint">
              Used for typed messages. Audio-only models reject plain text.
            </p>
          </div>

          <div class="field">
            <label class="field-label">Voice chat model</label>
            <select v-model="settings.chatModel" class="field-input">
              <option
                v-for="opt in VOICE_CHAT_MODEL_OPTIONS"
                :key="opt.value"
                :value="opt.value"
              >
                {{ opt.label }}
              </option>
            </select>
            <p class="field-hint">
              Used when you send a recording; must accept audio input.
            </p>
          </div>

          <div class="field">
            <label class="field-label">Assistant voice (TTS model)</label>
            <select v-model="settings.ttsModel" class="field-input">
              <option
                v-for="opt in TTS_MODEL_OPTIONS"
                :key="opt.value"
                :value="opt.value"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div class="field">
            <label class="field-label">Assistant voice (persona)</label>
            <select v-model="settings.ttsVoice" class="field-input">
              <option
                v-for="opt in TTS_VOICE_OPTIONS"
                :key="opt.value"
                :value="opt.value"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <hr class="divider" />

          <button class="danger-btn" @click="chat.clearMessages()">
            Clear conversation
          </button>
        </div>
      </aside>
    </div>
  </Transition>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.3);
  z-index: 100;
  display: flex;
  justify-content: flex-end;
}

.panel {
  width: 100%;
  max-width: 420px;
  background: var(--surface);
  display: flex;
  flex-direction: column;
  box-shadow: var(--shadow-lg);
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.panel-header h2 {
  font-size: 18px;
  font-weight: 600;
}

.close-btn {
  font-size: 28px;
  line-height: 1;
  color: var(--text-secondary);
  padding: 0 4px;
}

.close-btn:hover {
  color: var(--text);
}

.panel-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field-hint {
  font-size: 12px;
  color: var(--text-secondary);
  line-height: 1.4;
  margin: 0;
}

.field-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.field-textarea,
.field-input {
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 10px 12px;
  font-size: 14px;
  background: var(--bg);
  outline: none;
  transition: border-color var(--transition);
}

.field-textarea:focus,
.field-input:focus {
  border-color: var(--accent);
}

.field-textarea {
  resize: vertical;
  min-height: 120px;
  line-height: 1.5;
  font-family: inherit;
}

.link-btn {
  align-self: flex-start;
  font-size: 13px;
  color: var(--accent);
  padding: 2px 0;
}

.link-btn:hover {
  text-decoration: underline;
}

.divider {
  border: none;
  border-top: 1px solid var(--border);
}

.danger-btn {
  padding: 10px 16px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  color: var(--danger);
  border: 1px solid var(--danger);
  transition: background var(--transition);
}

.danger-btn:hover {
  background: #fef2f2;
}

.drawer-enter-active,
.drawer-leave-active {
  transition: opacity 0.2s ease;
}

.drawer-enter-from,
.drawer-leave-to {
  opacity: 0;
}

.drawer-enter-active .panel,
.drawer-leave-active .panel {
  transition: transform 0.2s ease;
}

.drawer-enter-from .panel,
.drawer-leave-to .panel {
  transform: translateX(100%);
}

@media (max-width: 640px) {
  .panel {
    max-width: 100%;
  }
}
</style>
