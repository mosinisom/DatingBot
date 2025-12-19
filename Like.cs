namespace DatingBot;

internal sealed class Like
{
    public long LikerChatId { get; init; }
    public long LikedChatId { get; init; }
    public DateTime LikedAt { get; init; }
}
