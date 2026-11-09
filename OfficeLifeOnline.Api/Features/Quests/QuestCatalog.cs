namespace OfficeLifeOnline.Api.Features.Quests;

public static class QuestCatalog
{
    private static readonly IReadOnlyDictionary<string, QuestDefinition> Items =
        new Dictionary<string, QuestDefinition>(StringComparer.Ordinal)
        {
            ["Q001"] = new("Q001", "Chấm công", "Đi đến khu vực chấm công để hoàn thành chấm công.", "", 300, 0, 0),
            ["Q002"] = new("Q002", "Pha nước", "Đến khu Pantry và tương tác với máy pha cà phê.", "coffee", 300, 0, 0),
            ["Q003"] = new("Q003", "In tài liệu", "Đến máy in và tương tác để hoàn thành nhiệm vụ.", "printer", 300, 0, 0),
            ["Q004"] = new("Q004", "Giao tài liệu", "Mang tài liệu đến đúng người hoặc khu vực được yêu cầu.", "", 300, 0, 0),
            ["Q005"] = new("Q005", "Tham gia họp", "Đến phòng họp và tương tác tại bàn họp.", "meeting", 300, 0, 0),
            ["Q006"] = new("Q006", "Nộp báo cáo", "Đến bàn quản lý và tương tác để nộp báo cáo.", "report", 300, 0, 0)
        };

    public static QuestDefinition Get(string id) =>
        Items.TryGetValue(id, out var quest)
            ? quest
            : throw new KeyNotFoundException($"Quest '{id}' was not found.");

    public static IReadOnlyCollection<QuestDefinition> All => Items.Values.ToArray();

    public static IReadOnlyCollection<QuestDefinition> SystemAssignable =>
        Items.Values.Where(x => !string.IsNullOrWhiteSpace(x.TargetId)).ToArray();
}