namespace OfficeLifeOnline.Api.Features.Quests;

public static class QuestCatalog
{
    private static readonly IReadOnlyDictionary<string, QuestDefinition> Items =
        new Dictionary<string, QuestDefinition>
        {
            ["Q001"] = new(
                "Q001",
                "Chấm công",
                "Đi đến máy chấm công để hoàn thành chấm công.",
                "attendance",
                300,
                0,
                0
            ),
            ["Q002"] = new(
                "Q002",
                "Pha nước",
                "Đến khu Pantry và pha một đồ uống.",
                "coffee",
                300,
                0,
                0
            ),
            ["Q003"] = new(
                "Q003",
                "In tài liệu",
                "Đến máy in và in tài liệu.",
                "printer",
                300,
                0,
                0
            ),
            ["Q004"] = new(
                "Q004",
                "Giao tài liệu",
                "Mang tài liệu đến người hoặc khu vực được yêu cầu.",
                "delivery",
                300,
                0,
                0
            ),
            ["Q005"] = new(
                "Q005",
                "Tham gia họp",
                "Đến phòng họp để tham gia cuộc họp.",
                "meeting",
                300,
                0,
                0
            ),
            ["Q006"] = new(
                "Q006",
                "Nộp báo cáo",
                "Đến bàn quản lý để nộp báo cáo.",
                "report",
                300,
                0,
                0
            )
        };

    public static QuestDefinition Get(string questId)
    {
        if (!Items.TryGetValue(questId, out var quest))
        {
            throw new KeyNotFoundException($"Quest '{questId}' was not found.");
        }

        return quest;
    }
}