/**
 * Decode a recorded blob (e.g. webm from MediaRecorder) and encode as WAV base64
 * for OpenAI Chat Completions `input_audio` (PCM 16-bit).
 */
export async function blobToWavBase64(blob: Blob): Promise<string> {
  const arrayBuffer = await blob.arrayBuffer()
  const ctx = new AudioContext()
  try {
    const audioBuffer = await ctx.decodeAudioData(arrayBuffer)
    const wav = encodeWavMono16bit(audioBuffer)
    const bytes = new Uint8Array(wav)
    let binary = ''
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i])
    return btoa(binary)
  } finally {
    await ctx.close()
  }
}

function encodeWavMono16bit(buffer: AudioBuffer): ArrayBuffer {
  const sampleRate = buffer.sampleRate
  const length = buffer.length
  const numCh = buffer.numberOfChannels
  const pcm = new Int16Array(length)
  const ch0 = buffer.getChannelData(0)
  if (numCh === 1) {
    for (let i = 0; i < length; i++) {
      const s = Math.max(-1, Math.min(1, ch0[i]))
      pcm[i] = s < 0 ? s * 0x8000 : s * 0x7fff
    }
  } else {
    for (let i = 0; i < length; i++) {
      let sum = ch0[i]
      for (let c = 1; c < numCh; c++) sum += buffer.getChannelData(c)[i]
      const s = Math.max(-1, Math.min(1, sum / numCh))
      pcm[i] = s < 0 ? s * 0x8000 : s * 0x7fff
    }
  }

  const dataSize = pcm.length * 2
  const out = new ArrayBuffer(44 + dataSize)
  const view = new DataView(out)
  let o = 0
  const wStr = (s: string) => {
    for (let i = 0; i < s.length; i++) view.setUint8(o++, s.charCodeAt(i))
  }
  wStr('RIFF')
  view.setUint32(o, 36 + dataSize, true)
  o += 4
  wStr('WAVE')
  wStr('fmt ')
  view.setUint32(o, 16, true)
  o += 4
  view.setUint16(o, 1, true)
  o += 2
  view.setUint16(o, 1, true)
  o += 2
  view.setUint32(o, sampleRate, true)
  o += 4
  view.setUint32(o, sampleRate * 2, true)
  o += 4
  view.setUint16(o, 2, true)
  o += 2
  view.setUint16(o, 16, true)
  o += 2
  wStr('data')
  view.setUint32(o, dataSize, true)
  o += 4
  new Int16Array(out, 44).set(pcm)
  return out
}
