from pathlib import Path


def replace(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text()
    if old not in text:
        raise SystemExit(f"pattern not found in {path}: {old[:100]!r}")
    p.write_text(text.replace(old, new, 1))


chat = "src/KnowledgeRag.Admin/src/features/chat/chat-page.tsx"
replace(
    chat,
    "import { ArchiveRestore, Bot, FileText, LoaderCircle, Maximize2, Menu, Minimize2, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Trash2, X } from 'lucide-react'",
    "import { ArchiveRestore, Bot, FileText, Maximize2, Menu, Minimize2, PanelLeftClose, PanelLeftOpen, PanelRightClose, PanelRightOpen, Trash2, X } from 'lucide-react'",
)
replace(
    chat,
    "  return [\n    `> **Agent workflow · ${state}**  `,\n    `> ${label}`,\n    detail ? `> ${escapeMarkdownText(detail)}` : '',\n  ].filter(Boolean).join('\\n')",
    "  const summary = [label, detail ? escapeMarkdownText(detail) : ''].filter(Boolean).join(' · ')\n  return `**Agent workflow · ${state}**  \\n${summary}`",
)
replace(
    chat,
    "                  {streaming && <span className='hidden items-center gap-1 rounded-full bg-primary/8 px-2 py-0.5 text-[10px] font-medium text-primary sm:inline-flex'><LoaderCircle className='size-3 animate-spin' />运行中</span>}\n",
    "",
)

thread = "src/KnowledgeRag.Admin/src/components/assistant-ui/elements/thread.aui.tsx"
replace(
    thread,
    '''              case "indicator":
                return (
                  <span
                    data-slot="aui_assistant-message-indicator"
                    className="animate-pulse font-sans"
                    aria-label="Assistant is working"
                  >
                    {"●"}
                  </span>
                );''',
    '''              case "indicator":
                return (
                  <span
                    data-slot="aui_assistant-message-indicator"
                    className="sr-only"
                    aria-label="Assistant is working"
                  >
                    Assistant is working
                  </span>
                );''',
)
replace(
    thread,
    '''      <ActionBarPrimitive.Reload asChild>
        <TooltipIconButton tooltip="Refresh">
          <RefreshCwIcon />
        </TooltipIconButton>
      </ActionBarPrimitive.Reload>''',
    '''      <AuiIf condition={(s) => s.thread.capabilities.reload}>
        <ActionBarPrimitive.Reload asChild>
          <TooltipIconButton tooltip="Refresh">
            <RefreshCwIcon />
          </TooltipIconButton>
        </ActionBarPrimitive.Reload>
      </AuiIf>''',
)
replace(
    thread,
    '''      <ActionBarPrimitive.Edit asChild>
        <TooltipIconButton tooltip="Edit" className="aui-user-action-edit">
          <PencilIcon />
        </TooltipIconButton>
      </ActionBarPrimitive.Edit>''',
    '''      <AuiIf condition={(s) => s.thread.capabilities.edit}>
        <ActionBarPrimitive.Edit asChild>
          <TooltipIconButton tooltip="Edit" className="aui-user-action-edit">
            <PencilIcon />
          </TooltipIconButton>
        </ActionBarPrimitive.Edit>
      </AuiIf>''',
)
replace(
    thread,
    '''const BranchPicker: FC<BranchPickerPrimitive.Root.Props> = ({
  className,
  ...rest
}) => {
  return (''',
    '''const BranchPicker: FC<BranchPickerPrimitive.Root.Props> = ({
  className,
  ...rest
}) => {
  const canSwitchToBranch = useAuiState((s) => s.thread.capabilities.switchToBranch);
  if (!canSwitchToBranch) return null;

  return (''',
)

markdown = "src/KnowledgeRag.Admin/src/components/assistant-ui/elements/markdown-text.tsx"
replace(
    markdown,
    '''  a: ({ className, ...props }) => (
    <a
      className={cn(
        "aui-md-a text-primary hover:text-primary/80 underline underline-offset-2",
        className,
      )}
      {...props}
    />
  ),''',
    '''  a: ({ className, href, children, ...props }) => {
    const citation = typeof href === "string" && href.startsWith("#kr-citation-");
    return (
      <a
        href={href}
        className={cn(
          citation
            ? "aui-md-citation text-primary font-medium no-underline hover:underline underline-offset-2"
            : "aui-md-a text-primary hover:text-primary/80 underline underline-offset-2",
          className,
        )}
        {...props}
      >
        {citation ? <>[{children}]</> : children}
      </a>
    );
  },''',
)

sources = "src/KnowledgeRag.Admin/src/components/assistant-ui/elements/sources.aui.tsx"
replace(
    sources,
    '''    const content = (
      <>
        <DocumentSourceIcon />
        <SourceTitle>{part.title}</SourceTitle>
      </>
    );''',
    '''    const content = (
      <>
        <span className="text-muted-foreground">引用</span>
        <DocumentSourceIcon />
        <SourceTitle>{part.title}</SourceTitle>
      </>
    );''',
)
replace(
    sources,
    '''          variant="secondary"
          className="gap-1.5 no-underline"''',
    '''          variant="ghost"
          aria-label={`查看引用 ${part.title}`}
          className="h-auto gap-1.5 rounded-none px-0 py-0 text-primary no-underline hover:bg-transparent hover:text-primary/80 hover:underline underline-offset-2"''',
)

Path("src/KnowledgeRag.Infrastructure/Conversations/CitationContract.cs").write_text(r'''using System.Text.RegularExpressions;
using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed record CitationNormalizationResult(
    string Answer,
    IReadOnlyList<SearchHit> Sources,
    int CitationCount,
    int InvalidCitationCount,
    bool HadCitationMarkers);

/// <summary>
/// Converts model-facing evidence references such as [E3] or [E1, E2] into stable
/// user-facing citations such as [1] [2], while only exposing evidence that was actually cited.
/// Invalid evidence references are removed rather than being presented as trusted sources.
/// </summary>
public static partial class CitationContract
{
    [GeneratedRegex(@"\[(?<refs>(?:[Ee]?\d{1,3})(?:\s*,\s*(?:[Ee]?\d{1,3}))*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationGroupRegex();

    [GeneratedRegex(@"\[(?:E|e)\d{1,3}[^\]\r\n]{0,64}\]", RegexOptions.CultureInvariant)]
    private static partial Regex MalformedEvidenceRegex();

    public static CitationNormalizationResult Normalize(string? answer, IReadOnlyList<SearchHit> evidence)
    {
        var text = answer ?? string.Empty;
        var matches = CitationGroupRegex().Matches(text);
        var malformedMatches = MalformedEvidenceRegex().Matches(text);
        if (matches.Count == 0 && malformedMatches.Count == 0)
            return new CitationNormalizationResult(text, [], 0, 0, false);

        var evidenceToCitation = new Dictionary<int, int>();
        var cited = new List<SearchHit>();
        var invalid = 0;

        var normalized = CitationGroupRegex().Replace(text, match =>
        {
            var emitted = new HashSet<int>();
            var output = new List<string>();
            foreach (var token in match.Groups["refs"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var raw = token;
                if (raw.Length > 0 && (raw[0] == 'E' || raw[0] == 'e')) raw = raw[1..];

                if (!int.TryParse(raw, out var evidenceNumber) || evidenceNumber < 1 || evidenceNumber > evidence.Count)
                {
                    invalid++;
                    continue;
                }

                if (!evidenceToCitation.TryGetValue(evidenceNumber, out var citationNumber))
                {
                    citationNumber = cited.Count + 1;
                    evidenceToCitation[evidenceNumber] = citationNumber;
                    cited.Add(evidence[evidenceNumber - 1]);
                }

                if (emitted.Add(citationNumber)) output.Add($"[{citationNumber}]");
            }

            return string.Join(" ", output);
        });

        normalized = MalformedEvidenceRegex().Replace(normalized, _ =>
        {
            invalid++;
            return string.Empty;
        });
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ").Trim();
        return new CitationNormalizationResult(normalized, cited, evidenceToCitation.Count, invalid, true);
    }
}
''')

tests = Path("tests/KnowledgeRag.Api.IntegrationTests/CitationContractTests.cs")
text = tests.read_text()
marker = '''    [Fact]
    public void Normalize_RemovesInvalidEvidenceReferences_AndDoesNotExposeThemAsSources()'''
addition = '''    [Fact]
    public void Normalize_ExpandsGroupedEvidenceReferences_IntoStableClickableMarkers()
    {
        var evidence = new[]
        {
            Hit(1, "a.pdf"),
            Hit(2, "b.pdf"),
            Hit(3, "c.pdf")
        };

        var result = CitationContract.Normalize("组合证据 [E2, e1]，补充 [E2, E3]。", evidence);

        Assert.Equal("组合证据 [1] [2]，补充 [1] [3]。", result.Answer);
        Assert.Equal(3, result.CitationCount);
        Assert.Equal(0, result.InvalidCitationCount);
        Assert.Equal(new[] { "b.pdf", "a.pdf", "c.pdf" }, result.Sources.Select(x => x.FileName));
    }

    [Fact]
    public void Normalize_GroupedReferenceWithInvalidMember_IsCountedInvalidAndRemoved()
    {
        var evidence = new[] { Hit(1, "a.pdf") };

        var result = CitationContract.Normalize("事实 [E1, E99]。", evidence);

        Assert.Equal("事实 [1]。", result.Answer);
        Assert.Equal(1, result.CitationCount);
        Assert.Equal(1, result.InvalidCitationCount);
        Assert.DoesNotContain("E99", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

'''
if marker not in text:
    raise SystemExit("citation test insertion marker not found")
tests.write_text(text.replace(marker, addition + marker, 1))

e2e = Path("src/KnowledgeRag.Admin/e2e/product-flows.spec.ts")
text = e2e.read_text()
old = "  await expect(sourceChip).toContainText('[1] handbook.pdf')\n  await expect(inlineCitation).toHaveText('1')\n"
new = "  await expect(sourceChip).toContainText('引用')\n  await expect(sourceChip).toContainText('[1] handbook.pdf')\n  await expect(sourceChip).toHaveAttribute('aria-label', '查看引用 [1] handbook.pdf')\n  await expect(inlineCitation).toHaveText('[1]')\n  await expect(page.locator('.aui-branch-picker-root')).toHaveCount(0)\n  await expect(page.getByRole('button', { name: 'Edit' })).toHaveCount(0)\n  await expect(page.getByRole('button', { name: 'Refresh' })).toHaveCount(0)\n"
if old not in text:
    raise SystemExit("e2e citation assertion marker not found")
e2e.write_text(text.replace(old, new, 1))

changelog = Path("CHANGELOG.md")
text = changelog.read_text()
marker = "### Fixed\n\n"
entry = (
    "- 修复 Chat Header 与 assistant turn 重复显示运行状态的问题；Agent Workflow 只在当前回答消息内以紧凑两行摘要呈现，通用 running indicator 仅保留无障碍文本，不再出现额外圆点。\n"
    "- 修复 External Store 未提供 branching/edit/reload 能力时仍出现 `2 / 2` BranchPicker、Edit / Refresh 控件的问题；assistant-ui UI 现在按 runtime capability 显示对应操作。\n"
    "- 改进 Chat 引用呈现：正文可信引用保留可见 `[n]` 并作为链接打开对应 Evidence，回答底部文档来源改为“引用 [n] 文件名”的轻量链接，不再使用填充 badge。\n"
    "- Structured Citation Contract 支持 `[E1, E2]` 组合 Evidence 引用并确定性展开为稳定用户引用；组合中的越界或畸形 Evidence 标记继续计为非法引用并触发既有 Final Citation Gate fail-closed。\n"
)
if marker not in text:
    raise SystemExit("changelog marker not found")
changelog.write_text(text.replace(marker, marker + entry, 1))
