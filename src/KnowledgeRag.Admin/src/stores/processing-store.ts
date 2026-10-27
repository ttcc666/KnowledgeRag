import { create } from 'zustand'
import type { DocumentProcessingNotification } from '@/lib/types'

type ProcessingState = {
  events: Record<string, DocumentProcessingNotification>
  update: (event: DocumentProcessingNotification) => void
  clear: (documentId: string) => void
}

export const useProcessingStore = create<ProcessingState>((set) => ({
  events: {},
  update: (event) => set((state) => ({ events: { ...state.events, [event.documentId]: event } })),
  clear: (documentId) => set((state) => {
    const next = { ...state.events }
    delete next[documentId]
    return { events: next }
  }),
}))
