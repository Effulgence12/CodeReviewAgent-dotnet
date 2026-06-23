namespace CodeReviewAgent.Console;

/// <summary>
/// 极简「行级」diff —— 用于在 CLI 中展示 propose_fix 修改前后的新旧对比。
///
/// 基于最长公共子序列（LCS）动态规划：先用 DP 表求出新旧两份文本按行的 LCS，
/// 再回溯还原出每一行是「未改动 / 删除（旧有新无）/ 新增（新有旧无）」。
/// 自己实现而非引第三方库，是因为算法足够简单、可在答辩中逐行讲解，
/// 且本项目只需「看个大概改了哪些行」，无需 Myers 等更优算法。
/// </summary>
internal static class DiffRenderer
{
    internal enum LineKind { Context, Added, Removed, Omitted }

    internal sealed record DiffLine(LineKind Kind, string Text);

    /// <summary>计算逐行 diff（未折叠）。</summary>
    public static IReadOnlyList<DiffLine> Diff(string oldText, string newText)
    {
        var a = SplitLines(oldText);
        var b = SplitLines(newText);

        // lcs[i, j] = a[i..] 与 b[j..] 的最长公共子序列长度（自右下向左上填表）。
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // 回溯：沿 DP 表从左上走到右下，还原增/删/未改动序列。
        var result = new List<DiffLine>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add(new DiffLine(LineKind.Context, a[x]));
                x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine(LineKind.Removed, a[x]));
                x++;
            }
            else
            {
                result.Add(new DiffLine(LineKind.Added, b[y]));
                y++;
            }
        }
        while (x < a.Length) result.Add(new DiffLine(LineKind.Removed, a[x++]));
        while (y < b.Length) result.Add(new DiffLine(LineKind.Added, b[y++]));
        return result;
    }

    /// <summary>
    /// 折叠：只保留每处改动前后各 <paramref name="context"/> 行，
    /// 远离改动的成片「未改动」行折叠为一条省略提示，避免整文件刷屏。
    /// </summary>
    public static IReadOnlyList<DiffLine> Collapse(IReadOnlyList<DiffLine> lines, int context = 2)
    {
        var keep = new bool[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind is LineKind.Added or LineKind.Removed)
            {
                var from = Math.Max(0, i - context);
                var to = Math.Min(lines.Count - 1, i + context);
                for (int j = from; j <= to; j++) keep[j] = true;
            }
        }

        var result = new List<DiffLine>();
        int omitted = 0;
        void FlushOmitted()
        {
            if (omitted > 0)
            {
                result.Add(new DiffLine(LineKind.Omitted, $"… {omitted} 行未改动 …"));
                omitted = 0;
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                FlushOmitted();
                result.Add(lines[i]);
            }
            else
            {
                omitted++;
            }
        }
        FlushOmitted();
        return result;
    }

    private static string[] SplitLines(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
