using Telegram.Bot.Types;

namespace DatingBot;

internal enum ConversationState
{
    None,
    WaitingForName,
    WaitingForInstitute,
    WaitingForPhoto
}

internal static class BotState
{
    internal static readonly Dictionary<long, ConversationState> UserStates = new();
    internal static readonly Dictionary<long, Student> UserDrafts = new();

    internal static void Reset(long chatId)
    {
        UserStates[chatId] = ConversationState.None;
        UserDrafts.Remove(chatId);
    }

    internal static void StartForm(long chatId)
    {
        UserStates[chatId] = ConversationState.WaitingForName;
        UserDrafts[chatId] = new Student { ChatId = chatId };
    }

    internal static void SaveDraft(long chatId, string? name = null, string? institute = null, string? photoFileId = null)
    {
        if (!UserDrafts.TryGetValue(chatId, out var draft))
        {
            draft = new Student { ChatId = chatId };
            UserDrafts[chatId] = draft;
        }

        if (name != null) draft.Name = name;
        if (institute != null) draft.Institute = institute;
        if (photoFileId != null) draft.PhotoFileId = photoFileId;
    }
}
