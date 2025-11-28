namespace DatingBot;

internal sealed class Student
{
    public long ChatId { get; init; }
    public string Name { get; set; } = null!;
    public string Institute { get; set; } = null!;
    public string? PhotoFileId { get; set; }
}
