import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { useAuthStore } from '@/stores/auth-store'
import type { DocumentProcessingNotification, KnowledgeDocument } from '@/lib/types'

const stageText: Record<string, string> = {
  queued: '已加入索引队列',
  processing: '开始处理',
  parsing: '正在解析文档',
  chunking: '正在切分文本',
  embedding: '正在生成向量',
  storing: '正在写入索引',
  completed: '索引完成',
  failed: '索引失败',
}

export function DocumentProcessingNotifications() {
  const queryClient = useQueryClient()
  const accessToken = useAuthStore((state) => state.accessToken)

  useEffect(() => {
    if (!accessToken) return
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/documents', { accessTokenFactory: () => useAuthStore.getState().accessToken })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('DocumentProcessingUpdated', (event: DocumentProcessingNotification) => {
      queryClient.setQueryData<KnowledgeDocument[]>(['documents', event.knowledgeBaseId], (current) =>
        current?.map((doc) => doc.id === event.documentId
          ? { ...doc, status: event.status, chunkCount: event.chunkCount, lastError: event.error, updatedAt: event.updatedAt }
          : doc))
      if (event.stage === 'completed') {
        toast.success(`${event.fileName} 向量化完成`)
        void queryClient.invalidateQueries({ queryKey: ['documents', event.knowledgeBaseId] })
      } else if (event.stage === 'failed') {
        toast.error(`${event.fileName} 处理失败：${event.error || '未知错误'}`)
        void queryClient.invalidateQueries({ queryKey: ['documents', event.knowledgeBaseId] })
      } else if (event.stage === 'queued') {
        toast.info(`${event.fileName}：${stageText[event.stage]}`)
      }
    })

    void connection.start().catch(() => undefined)
    return () => { void connection.stop() }
  }, [accessToken, queryClient])

  return null
}
