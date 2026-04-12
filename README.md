# Voxsy

AI-powered English conversation partner for speaking and pronunciation practice.

Chat with an AI tutor via text or voice — it responds naturally, gently corrects your grammar, and helps you improve over time.

## Architecture

- **Frontend:** Vue 3 + TypeScript + Vite + Pinia + CSS
- **Backend:** ASP.NET Core (.NET 10) — thin BFF that proxies OpenAI Chat Completions (streaming) and speech synthesis

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/)
- An [OpenAI API key](https://platform.openai.com/api-keys)

## Setup

### Backend

```bash
cd backend

# Store your OpenAI API key securely (one-time)
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-key-here"

# Run
dotnet run
```

The API starts at `http://localhost:5041`.

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Opens at `http://localhost:5173`.

## Configuration

### Backend (`appsettings.json`)

| Key | Default | Description |
|-----|---------|-------------|
| `OpenAI:ApiKey` | — | OpenAI API key (use User Secrets or env var `OpenAI__ApiKey`) |
| `OpenAI:BaseUrl` | `https://api.openai.com/v1/` | API base URL (compatible providers work too) |
| `OpenAI:ChatModel` | `gpt-4o-mini` | Default chat model if the client omits `model` |
| `OpenAI:TtsModel` | `gpt-4o-mini-tts` | Default TTS model for `/api/speech` |
| `OpenAI:TtsVoice` | `alloy` | Default voice for `/api/speech` |
| `Cors:Origins` | `["http://localhost:5173"]` | Allowed CORS origins |

### Frontend (`.env`)

| Key | Default | Description |
|-----|---------|-------------|
| `VITE_API_BASE_URL` | `http://localhost:5041` | Backend API URL |

The API base URL can also be changed at runtime in the Settings panel.

## API Endpoints

### `POST /api/chat/stream`

Proxies chat completions with streaming. Request body:

```json
{
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user", "content": "Hello!" }
  ]
}
```

Response: SSE stream in OpenAI format (`text/event-stream`). The `model` and `stream: true` fields are injected by the backend when missing.

### `POST /api/speech`

Proxies OpenAI `audio/speech` for assistant TTS. JSON body: `input` (required), optional `model`, `voice`, `format` (e.g. `mp3`). Returns raw audio bytes.

Voice messages in the app are sent as `input_audio` in chat completions (no separate transcription API).
