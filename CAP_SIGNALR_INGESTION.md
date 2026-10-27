# CAP + SignalR asynchronous document ingestion

## Flow

1. `POST /api/knowledge-bases/{knowledgeBaseId}/documents` validates and persists the original file.
2. A `DocumentEntity` is created with `Queued` status.
3. `knowledge.document.index.requested.v1` is published through CAP.
4. With PostgreSQL, the document row and CAP outbox record are committed in the same EF Core transaction.
5. `DocumentIndexingSubscriber` consumes the event and performs parsing, chunking, embeddings, vector upsert, and chunk persistence.
6. Progress is sent through the authenticated SignalR hub at `/hubs/documents`.
7. The React admin client automatically receives notifications for the current user and refreshes the affected document list.

## CAP modes

### Local / SQLite

Default configuration uses:

- `DotNetCore.CAP.InMemoryStorage`
- `Savorboard.CAP.InMemoryMessageQueue`

This is intended for local development and tests only. It does not survive process restarts.

### PostgreSQL + RabbitMQ

Set:

```env
DATABASE_PROVIDER=PostgreSql
CAP_TRANSPORT=rabbitmq
RABBITMQ_HOST=rabbitmq
RABBITMQ_PORT=5672
RABBITMQ_USER=knowledge_rag
RABBITMQ_PASSWORD=knowledge_rag_dev_password
```

Start infrastructure with:

```bash
docker compose --profile postgres --profile cap up -d postgres rabbitmq
```

CAP stores its outbox/inbox tables under PostgreSQL schema `cap` and uses RabbitMQ for transport.

## SignalR notification stages

- `queued`
- `processing`
- `parsing`
- `chunking`
- `embedding`
- `storing`
- `completed`
- `failed`

Notifications are advisory: a SignalR delivery failure is logged but does not fail or roll back document indexing. The durable source of truth remains `Documents.Status` plus CAP's persisted message state.

## Retry / idempotency

CAP retries failed subscriber executions. The subscriber returns immediately when a document is already `Ready`. Before a retry re-indexes a non-ready document, existing document chunks/vectors are removed so the new attempt rebuilds the index from the stored source file.
