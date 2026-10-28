using KnowledgeRag.Domain.Entities;

namespace KnowledgeRag.Infrastructure.Documents;

public static class DocumentIndexLifecycle
{
    public static bool IsSearchable(DocumentStatus status)
        => status is DocumentStatus.Ready or DocumentStatus.Reindexing;

    public static bool IsBusy(DocumentStatus status)
        => status is DocumentStatus.Queued or DocumentStatus.Processing or DocumentStatus.Reindexing;

    public static DocumentStatus BeginReindex(DocumentStatus status)
        => status == DocumentStatus.Ready
            ? DocumentStatus.Reindexing
            : throw new InvalidOperationException("只有 Ready 文档可以开始蓝绿重新索引。");

    public static DocumentStatus RestoreSearchableVersionAfterReindexFailure()
        => DocumentStatus.Ready;
}
