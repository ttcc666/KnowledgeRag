from pathlib import Path


def patch(path: str, replacements: list[tuple[str, str]]) -> None:
    file = Path(path)
    text = file.read_text()
    for old, new in replacements:
        if old not in text:
            raise SystemExit(f"patch target not found in {path}: {old[:80]}")
        text = text.replace(old, new, 1)
    file.write_text(text)


patch("src/KnowledgeRag.Infrastructure/Governance/GovernanceService.cs", [
    ("        var cost = await runs.SumAsync(x => x.EstimatedCostUsd, ct);", "        var cost = (await runs.Select(x => x.EstimatedCostUsd).ToListAsync(ct)).Sum();"),
    ("""        return await db.Set<RagRunEntity>().AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);""", """        var rows = await db.Set<RagRunEntity>().AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToList();"""),
    ("""        return await db.Set<AuditLogEntity>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToListAsync(ct);""", """        var rows = await db.Set<AuditLogEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 1000)).ToList();"""),
    ("""        return await db.Set<PromptProfileEntity>().AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);""", """        var rows = await db.Set<PromptProfileEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.UpdatedAt).ToList();"""),
    ("        return await db.Set<WebhookSubscriptionEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);", """        var rows = await db.Set<WebhookSubscriptionEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.UpdatedAt).ToList();"""),
    ("        return await db.Set<EvaluationDatasetEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);", """        var rows = await db.Set<EvaluationDatasetEntity>().AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(ct);
        return rows.OrderByDescending(x => x.UpdatedAt).ToList();"""),
    ("        return await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).OrderBy(x => x.CreatedAt).ToListAsync(ct);", """        var rows = await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).ToListAsync(ct);
        return rows.OrderBy(x => x.CreatedAt).ToList();"""),
    ("        return await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);", """        var rows = await query.ToListAsync(ct);
        return rows.OrderByDescending(x => x.CreatedAt).Take(200).ToList();"""),
])

patch("src/KnowledgeRag.Infrastructure/Governance/EvaluationService.cs", [
    ("        var cases = await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).OrderBy(x => x.CreatedAt).ToListAsync(ct);", """        var caseRows = await db.Set<EvaluationCaseEntity>().AsNoTracking().Where(x => x.DatasetId == datasetId).ToListAsync(ct);
        var cases = caseRows.OrderBy(x => x.CreatedAt).ToList();"""),
])

patch("src/KnowledgeRag.Infrastructure/Conversations/ConversationService.cs", [
    ("""        var runs = db.Set<RagRunEntity>().AsNoTracking().Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.TenantId == tenantId && x.Status == \"completed\");
        if (profile.DailyRunLimit > 0)
        {
            var start = DateTimeOffset.UtcNow.Date;
            var today = await runs.CountAsync(x => x.CreatedAt >= start, ct);
            if (today >= profile.DailyRunLimit) throw new InvalidOperationException(\"该知识库已达到今日 RAG Run 配额。\");
        }
        if (profile.MonthlyBudgetUsd > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var spend = await runs.Where(x => x.CreatedAt >= start).SumAsync(x => x.EstimatedCostUsd, ct);
            if (spend >= profile.MonthlyBudgetUsd) throw new InvalidOperationException(\"该知识库已达到本月 AI 成本预算。\");
        }""", """        var runs = await db.Set<RagRunEntity>().AsNoTracking()
            .Where(x => x.KnowledgeBaseId == knowledgeBaseId && x.TenantId == tenantId && x.Status == \"completed\")
            .Select(x => new { x.CreatedAt, x.EstimatedCostUsd })
            .ToListAsync(ct);
        if (profile.DailyRunLimit > 0)
        {
            var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
            var today = runs.Count(x => x.CreatedAt >= start);
            if (today >= profile.DailyRunLimit) throw new InvalidOperationException(\"该知识库已达到今日 RAG Run 配额。\");
        }
        if (profile.MonthlyBudgetUsd > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var spend = runs.Where(x => x.CreatedAt >= start).Sum(x => x.EstimatedCostUsd);
            if (spend >= profile.MonthlyBudgetUsd) throw new InvalidOperationException(\"该知识库已达到本月 AI 成本预算。\");
        }"""),
])
