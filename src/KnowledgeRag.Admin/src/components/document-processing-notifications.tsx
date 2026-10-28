import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { useAuthStore } from '@/stores/auth-store'
import { useProcessingStore } from '@/stores/processing-store'
import type { DocumentProcessingNotification, KnowledgeDocument } from '@/lib/types'

const stageText: Record<string, string> = {
  queued: '已加入索引队列',
  retry_queued: '已重新加入索引队列',
  reindex_queued: '已加入重新索引队列',
  processing: '开始处理',
  parsing: '正在解析文档',
  chunking: '正在切分文本',
  embedding: '正在生成向量',
  storing: '正在写入索引',
  completed: '索引完成',
  cancelled: '处理已取消',
  failed: '索引失败',
}

export function DocumentProcessingNotifications() {
  const queryClient = useQueryClient()
  const accessToken = useAuthStore((state) => state.accessToken)
  const updateProcessing = useProcessingStore((state) => state.update)

  useEffect(() => {
    if (!accessToken) return
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/documents', { accessTokenFactory: () => useAuthStore.getState().accessToken })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('DocumentProcessingUpdated', (event: DocumentProcessingNotification) => {
      updateProcessing(event)
      queryClient.setQueryData<KnowledgeDocument[]>(['documents', event.knowledgeBaseId], (current) =>
        current?.map((doc) => doc.id === event.documentId
          ? { ...doc, status: event.status, chunkCount: event.chunkCount, lastError: event.error, updatedAt: event.updatedAt }
          : doc))

      if (event.stage === 'completed') toast.success(`${event.fileName} 向量化完成`)
      else if (event.stage === 'failed') toast.error(`${event.fileName} 处理失败：${event.error || '未知错误'}`)
      else if (event.stage === 'cancelled') toast.info(`${event.fileName}：处理已取消`)
      else if (['queued', 'retry_queued', 'reindex_queued'].includes(event.stage)) toast.info(`${event.fileName}：${stageText[event.stage]}`)

      if (['completed', 'failed', 'cancelled', 'retry_queued', 'reindex_queued'].includes(event.stage)) {
        void queryClient.invalidateQueries({ queryKey: ['documents', event.knowledgeBaseId] })
        void queryClient.invalidateQueries({ queryKey: ['ingestion-jobs'] })
        void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      }
    })

    // React StrictMode intentionally mounts, cleans up and mounts effects again in development.
    // Do not stop a SignalR connection while its initial negotiation is still in progress: wait
    // for start() to settle first, then stop the connection created by this effect instance.
    const startPromise = connection.start().catch(() => undefined)
    return () => {
      void startPromise
        .then(() => connection.stop())
        .catch(() => undefined)
    }
  }, [accessToken, queryClient, updateProcessing])

  return null
}
