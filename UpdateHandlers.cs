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

                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new []
                    {
                        InlineKeyboardButton.WithCallbackData("ИГЗ", "inst:ИГЗ"),
                        InlineKeyboardButton.WithCallbackData("ИЕН", "inst:ИЕН"),
                        InlineKeyboardButton.WithCallbackData("ИИиД", "inst:ИИиД"),
                        InlineKeyboardButton.WithCallbackData("ИИиС", "inst:ИИиС"),

                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("ИМИТиФ", "inst:ИМИТиФ"),
                        InlineKeyboardButton.WithCallbackData("ИНиГ", "inst:ИНиГ"),
                        InlineKeyboardButton.WithCallbackData("ИППСТ", "inst:ИППСТ"),
                        InlineKeyboardButton.WithCallbackData("ИПСУБ", "inst:ИПСУБ"),                        
                    },
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("ИСК", "inst:ИСК"),
                        InlineKeyboardButton.WithCallbackData("ИУФФиЖ", "inst:ИУФФиЖ"),
                        InlineKeyboardButton.WithCallbackData("ИФКиС", "inst:ИФКиС"),
                        InlineKeyboardButton.WithCallbackData("ИЭиУ", "inst:ИЭиУ"),                       
                    },
                    new []
                    {
                        InlineKeyboardButton.WithCallbackData("ИЯЛ", "inst:ИЯЛ"),
                        InlineKeyboardButton.WithCallbackData("МКПО", "inst:МКПО"),                        
                    }
                });

                await _bot.SendMessage(msg.Chat, "Выбери, пожалуйста, свой институт", replyMarkup: inlineKeyboard);
                break;
            case ConversationState.WaitingForInstitute:
                // ожидание выбора института, текст здесь игнорируем
                break;
            case ConversationState.WaitingForDescription:
                BotState.SaveDraft(chatId, description: msg.Text);
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
            case "/yo":
                await _bot.SendMessage(msg.Chat, "Twenty One");
                await _bot.SendMessage(msg.Chat, "Twenty One");
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

        var text = $"Твоя анкета:\nИмя: {student.Name}\nИнститут: {student.Institute}";
        if (!string.IsNullOrWhiteSpace(student.Description))
        {
            text += $"\nОписание: {student.Description}";
        }

        if (!string.IsNullOrWhiteSpace(student.PhotoFileId))
        {
            await _bot.SendPhoto(msg.Chat, student.PhotoFileId, caption: text);
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

        _database.SaveStudent(draft);
        BotState.Reset(chatId);

        await _bot.SendMessage(msg.Chat, "Спасибо! Твоя анкета сохранена.");
    }

    public async Task HandleUpdateAsync(Update update)
    {
        switch (update)
        {
            case { CallbackQuery: { } callbackQuery }: await HandleCallbackQueryAsync(callbackQuery); break;
            case { PollAnswer: { } pollAnswer }: await HandlePollAnswerAsync(pollAnswer); break;
            default: Console.WriteLine($"Received unhandled update {update.Type}"); break;
        };
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Data is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id);
            return;
        }

        if (callbackQuery.Data.StartsWith("inst:"))
        {
            var institute = callbackQuery.Data["inst:".Length..];
            var chatId = callbackQuery.Message!.Chat.Id;

            BotState.SaveDraft(chatId, institute: institute);
            BotState.UserStates[chatId] = ConversationState.WaitingForDescription;

            await _bot.AnswerCallbackQuery(callbackQuery.Id, $"Институт: {institute}");

            // обновляем сообщение с кнопками, чтобы убрать клавиатуру
            await _bot.EditMessageReplyMarkup(callbackQuery.Message.Chat, callbackQuery.Message.MessageId, replyMarkup: null);

            await _bot.SendMessage(callbackQuery.Message.Chat, "Напиши, пожалуйста, текст своей анкеты.");
            return;
        }

        await _bot.AnswerCallbackQuery(callbackQuery.Id);
    }

    private async Task HandlePollAnswerAsync(PollAnswer pollAnswer)
    {
        if (pollAnswer.User != null)
            await _bot.SendMessage(pollAnswer.User.Id, $"You voted for option(s) id [{string.Join(',', pollAnswer.OptionIds)}]");
    }
}
