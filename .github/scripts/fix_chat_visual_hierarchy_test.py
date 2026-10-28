from pathlib import Path

p = Path('src/KnowledgeRag.Admin/e2e/product-flows.spec.ts')
text = p.read_text()
old = "  await expect(sourceChip).toContainText('[1] handbook.pdf')\n"
new = "  await expect(sourceChip).toContainText('[1]')\n  await expect(sourceChip).toContainText('handbook.pdf')\n"
if old not in text:
    raise SystemExit('source assertion marker not found')
p.write_text(text.replace(old, new, 1))
