/** Chat models selectable in settings (Chat Completions). */
export const CHAT_MODEL_OPTIONS: { value: string; label: string; group: string }[] = [
  { value: 'gpt-4o-mini', label: 'GPT-4o mini', group: 'Text' },
  { value: 'gpt-4o', label: 'GPT-4o', group: 'Text' },
  { value: 'gpt-audio-mini', label: 'GPT Audio mini', group: 'Voice (audio input)' },
  { value: 'gpt-audio', label: 'GPT Audio', group: 'Voice (audio input)' },
  { value: 'gpt-4o-audio-preview', label: 'GPT-4o Audio (preview)', group: 'Voice (audio input)' },
]

export const TEXT_CHAT_MODEL_OPTIONS = CHAT_MODEL_OPTIONS.filter(
  (o) => o.group === 'Text',
)

export const VOICE_CHAT_MODEL_OPTIONS = CHAT_MODEL_OPTIONS.filter(
  (o) => o.group !== 'Text',
)

/** Text-to-speech models (Audio API speech). */
export const TTS_MODEL_OPTIONS: { value: string; label: string }[] = [
  { value: 'gpt-4o-mini-tts', label: 'GPT-4o mini TTS' },
  { value: 'tts-1', label: 'TTS-1' },
  { value: 'tts-1-hd', label: 'TTS-1 HD' },
]

/** Voices supported by tts-1 / tts-1-hd / gpt-4o-mini-tts (standard set). */
export const TTS_VOICE_OPTIONS: { value: string; label: string }[] = [
  { value: 'alloy', label: 'Alloy' },
  { value: 'echo', label: 'Echo' },
  { value: 'fable', label: 'Fable' },
  { value: 'onyx', label: 'Onyx' },
  { value: 'nova', label: 'Nova' },
  { value: 'shimmer', label: 'Shimmer' },
]

export function usesNativeAudioInput(model: string): boolean {
  const m = model.toLowerCase()
  return (
    m.startsWith('gpt-audio')
    || m.includes('audio-preview')
    || m.includes('4o-audio')
  )
}
