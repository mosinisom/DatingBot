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
            case "/random":
                await ShowRandomProfileAsync(msg.Chat.Id);
                break;
        }
    }

    private InlineKeyboardMarkup BuildProfileKeyboard(Student student)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🩵", $"p:like:{student.ChatId}"),
                InlineKeyboardButton.WithCallbackData("🚩", $"p:report:{student.ChatId}"),
                InlineKeyboardButton.WithCallbackData("➡️", "p:next"),
            }
        });
    }

    private async Task ShowRandomProfileAsync(long chatId)
    {
        var randomStudent = _database.GetRandomStudent(chatId);

        if (randomStudent == null)
        {
            await _bot.SendMessage(chatId, "Пока нет других анкет.");
            return;
        }

        await SendProfileAsync(chatId, randomStudent, BuildProfileKeyboard(randomStudent), header: "Случайная анкета:");
    }

    private async Task SendProfileAsync(long chatId, Student student, InlineKeyboardMarkup? keyboard, string? header = null)
    {
        var likesCount = _database.GetLikesCount(student.ChatId);
        var likesText = likesCount > 0 ? $"❤️ {likesCount}" : "";

        var text = $"{student.Name}\n" +
                   $"{student.Institute}\n" +
                   $"{student.Description ?? " "}";

        if (!string.IsNullOrEmpty(likesText))
        {
            text += $"\n\n{likesText}";
        }

        if (!string.IsNullOrEmpty(header))
        {
            text = $"{header}\n{text}";
        }

        if (!string.IsNullOrEmpty(student.PhotoFileId))
        {
            await _bot.SendPhoto(chatId, InputFile.FromFileId(student.PhotoFileId), caption: text, replyMarkup: keyboard);
        }
        else
        {
            await _bot.SendMessage(chatId, text, replyMarkup: keyboard);
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

        await SendProfileAsync(chatId, student, keyboard: null, header: "Твоя анкета:");
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

        draft.Username = msg.From?.Username;

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
        }
        ;
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Data is null)
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id);
            return;
        }

        if (callbackQuery.Data == "p:next")
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id);
            var chatId = callbackQuery.Message!.Chat.Id;
            await ShowRandomProfileAsync(chatId);
            return;
        }

        if (callbackQuery.Data.StartsWith("p:like:", StringComparison.Ordinal))
        {
            var chatId = callbackQuery.Message!.Chat.Id;
            var likedChatIdStr = callbackQuery.Data["p:like:".Length..];
            if (!long.TryParse(likedChatIdStr, out var likedChatId))
            {
                await _bot.AnswerCallbackQuery(callbackQuery.Id, "Ошибка при обработке лайка.");
                return;
            }

            await HandleLikeAsync(callbackQuery, chatId, likedChatId);
            return;
        }

        if (callbackQuery.Data.StartsWith("p:likeBack:", StringComparison.Ordinal))
        {
            var chatId = callbackQuery.Message!.Chat.Id;
            var likedChatIdStr = callbackQuery.Data["p:likeBack:".Length..];
            if (!long.TryParse(likedChatIdStr, out var likedChatId))
            {
                await _bot.AnswerCallbackQuery(callbackQuery.Id, "Ошибка при обработке лайка.");
                return;
            }

            await HandleLikeBackAsync(callbackQuery, chatId, likedChatId);
            return;
        }

        if (callbackQuery.Data.StartsWith("p:skip:", StringComparison.Ordinal))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "Анкета пропущена.");
            return;
        }

        if (callbackQuery.Data.StartsWith("p:report:", StringComparison.Ordinal))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "Жалоба отправлена (заглушка).");
            var chatId = callbackQuery.Message!.Chat.Id;
            await ShowRandomProfileAsync(chatId);
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

    private async Task HandleLikeAsync(CallbackQuery callbackQuery, long likerChatId, long likedChatId)
    {
        if (!_database.CanLike(likerChatId, likedChatId))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "Ты уже лайкал(а) эту анкету сегодня. Попробуй завтра!");
            await ShowRandomProfileAsync(likerChatId);
            return;
        }

        _database.SaveLike(likerChatId, likedChatId);

        if (_database.HasMutualLike(likerChatId, likedChatId))
        {
            await HandleMatchAsync(likerChatId, likedChatId);
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "🎉 Это матч!");
        }
        else
        {
            await SendLikeNotificationAsync(likerChatId, likedChatId);
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "❤️ Лайк отправлен!");
        }

        await ShowRandomProfileAsync(likerChatId);
    }

    private async Task HandleLikeBackAsync(CallbackQuery callbackQuery, long likerChatId, long likedChatId)
    {
        if (!_database.CanLike(likerChatId, likedChatId))
        {
            await _bot.AnswerCallbackQuery(callbackQuery.Id, "Ты уже лайкал(а) эту анкету сегодня!");
            return;
        }

        _database.SaveLike(likerChatId, likedChatId);

        await HandleMatchAsync(likerChatId, likedChatId);
        await _bot.AnswerCallbackQuery(callbackQuery.Id, "🎉 Это матч!");
    }

    private async Task SendLikeNotificationAsync(long likerChatId, long likedChatId)
    {
        var likerStudent = _database.GetStudentByChatId(likerChatId);
        if (likerStudent == null)
            return;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("💙 Лайкнуть в ответ", $"p:likeBack:{likerChatId}"),
                InlineKeyboardButton.WithCallbackData("❌ Пропустить", $"p:skip:{likerChatId}"),
            }
        });

        await SendProfileAsync(likedChatId, likerStudent, keyboard, header: "💌 Тебя лайкнули!");
    }

    private async Task HandleMatchAsync(long user1ChatId, long user2ChatId)
    {
        var student1 = _database.GetStudentByChatId(user1ChatId);
        var student2 = _database.GetStudentByChatId(user2ChatId);

        if (student1 == null || student2 == null)
            return;

        var username1 = !string.IsNullOrEmpty(student1.Username) ? $"@{student1.Username}" : "не указан";
        var username2 = !string.IsNullOrEmpty(student2.Username) ? $"@{student2.Username}" : "не указан";

        var matchMessage1 = $"🎉 У вас взаимный лайк с {student2.Name}!\n\n" +
                           $"💬 Напиши ему/ей: {username2}";
        await _bot.SendMessage(user1ChatId, matchMessage1);

        var matchMessage2 = $"🎉 У вас взаимный лайк с {student1.Name}!\n\n" +
                           $"💬 Напиши ему/ей: {username1}";
        await _bot.SendMessage(user2ChatId, matchMessage2);
    }
}
