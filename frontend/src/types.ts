export interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  content: string
  source?: 'text' | 'voice'
  audioUrl?: string
}
