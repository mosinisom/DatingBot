using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DatingBot;

internal sealed class UpdateHandlers
{
    private readonly ITelegramBotClient _bot;
    private readonly Database _database;

    public UpdateHandlers(ITelegramBotClient bot, Database database)
    {
        _bot = bot;
        _database = database;
    }

    public Task HandleErrorAsync(Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        Console.WriteLine(exception);
        return Task.Delay(2000, cancellationToken);
    }

    public async Task HandleMessageAsync(Message msg, UpdateType type)
    {
        if (msg.Photo is { Length: > 0 })
        {
            await HandlePhotoMessageAsync(msg);
            return;
        }

        if (msg.Text is not { } text)
            return;

        if (text.StartsWith('/'))
        {
            var space = text.IndexOf(' ');
            if (space < 0) space = text.Length;
            var command = text[..space].ToLower();
            await HandleCommandAsync(command, text[space..].TrimStart(), msg);
        }
        else
        {
            await HandleTextMessageAsync(msg);
        }
    }

    private async Task HandleTextMessageAsync(Message msg)
    {
        if (msg.Chat.Id == 0) return;
        var chatId = msg.Chat.Id;
        BotState.UserStates.TryGetValue(chatId, out var state);

        if (msg.Text is null)
        {
            Console.WriteLine($"Received non-text message in {msg.Chat}");
            return;
        }

        switch (state)
        {
            case ConversationState.WaitingForName:
                BotState.SaveDraft(chatId, name: msg.Text);
                BotState.UserStates[chatId] = ConversationState.WaitingForInstitute;
                await _bot.SendMessage(msg.Chat, "Напиши, пожалуйста, свой институт.");
                break;
            case ConversationState.WaitingForInstitute:
                BotState.SaveDraft(chatId, institute: msg.Text);
                BotState.UserStates[chatId] = ConversationState.WaitingForPhoto;
                await _bot.SendMessage(msg.Chat, "Отправь, пожалуйста, своё фото одним сообщением.");
                break;
            default:
                await HandleCommandAsync("/start", "", msg);
                break;
        }
    }

    private async Task HandleCommandAsync(string command, string args, Message msg)
    {
        switch (command)
        {
            case "/start":
                BotState.StartForm(msg.Chat.Id);
                await _bot.SendMessage(msg.Chat,
                    "Привет! Я бот знакомств для студентов УдГУ. Как тебя зовут?",
                    replyMarkup: new ReplyKeyboardRemove());
                break;
            case "/cancel":
                BotState.Reset(msg.Chat.Id);
                await _bot.SendMessage(msg.Chat, "Анкета отменена. Если захочешь начать снова — напиши /start.");
                break;
            case "/me":
                await HandleMeCommandAsync(msg);
                break;
        }
    }

    private async Task HandleMeCommandAsync(Message msg)
    {
        var chatId = msg.Chat.Id;
        var student = _database.GetStudentByChatId(chatId);
        if (student is null)
        {
            await _bot.SendMessage(msg.Chat, "Я пока не нашёл твою анкету. Попробуй заполнить её с команды /start.");
            return;
        }

        var (name, institute, photoFileId) = student.Value;
        var text = $"Твоя анкета:\nИмя: {name}\nИнститут: {institute}";

        if (!string.IsNullOrWhiteSpace(photoFileId))
        {
            await _bot.SendPhoto(msg.Chat, photoFileId, caption: text);
        }
        else
        {
            await _bot.SendMessage(msg.Chat, text);
        }
    }

    private async Task HandlePhotoMessageAsync(Message msg)
    {
        var photos = msg.Photo;
        if (photos is null || photos.Length == 0)
            return;

        var chatId = msg.Chat.Id;
        BotState.UserStates.TryGetValue(chatId, out var state);
        if (state != ConversationState.WaitingForPhoto)
            return;

        var fileId = photos.MaxBy(p => p.FileSize)?.FileId ?? photos.Last().FileId;
        BotState.SaveDraft(chatId, photoFileId: fileId);

        if (!BotState.UserDrafts.TryGetValue(chatId, out var draft) || string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Institute))
        {
            await _bot.SendMessage(msg.Chat, "Что-то пошло не так с анкетой. Попробуй ещё раз с команды /start.");
            BotState.Reset(chatId);
            return;
        }

        _database.SaveStudent(chatId, draft.Name!, draft.Institute!, draft.PhotoFileId);
        BotState.Reset(chatId);

        await _bot.SendMessage(msg.Chat, "Спасибо! Твоя анкета сохранена.");
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        await _bot.AnswerCallbackQuery(callbackQuery.Id, $"You selected {callbackQuery.Data}");
        await _bot.SendMessage(callbackQuery.Message!.Chat, $"Received callback from inline button {callbackQuery.Data}");
    }

    private async Task HandlePollAnswerAsync(PollAnswer pollAnswer)
    {
        if (pollAnswer.User != null)
            await _bot.SendMessage(pollAnswer.User.Id, $"You voted for option(s) id [{string.Join(',', pollAnswer.OptionIds)}]");
    }
}
